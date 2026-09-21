using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BotNC.App.Services;

public sealed class HpAudioAlertService
{
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const double PrefixDetectionThreshold = 0.66;
    private const double FullDetectionThreshold = 0.62;
    private const double NearDetectionThreshold = 0.58;
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan NearCandidateSeparation = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan NearCandidateWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TelemetryInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PeakWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AudioSignalRecentWindow = TimeSpan.FromSeconds(15);
    private const double AudibleRmsThreshold = 0.0001;

    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private readonly object _telemetrySync = new();
    private int _pendingAlert;
    private int _nearCandidateCount;
    private double _pendingConfidence;
    private double _currentConfidence;
    private double _recentPeakConfidence;
    private long _lastAlertTicks;
    private long _lastNearCandidateTicks;
    private long _firstNearCandidateTicks;
    private long _lastTelemetryTicks;
    private long _peakWindowStartedTicks;
    private long _capturedBufferCount;
    private long _audibleBufferCount;
    private long _lastAudibleTicks;
    private double _currentAudioRms;
    private double _peakAudioRms;
    private volatile bool _armed;
    private volatile bool _healthy;

    public event Action<string>? Status;
    public event Action<HpAudioMonitorSnapshot>? Telemetry;

    public bool Armed
    {
        get => _armed;
        set
        {
            var changed = _armed != value;
            _armed = value;
            if (!value)
            {
                Interlocked.Exchange(ref _pendingAlert, 0);
                ResetNearCandidates();
            }

            if (changed)
            {
                Status?.Invoke(value
                    ? "Proteção de HP ARMADA e aguardando o alerta sonoro."
                    : "Proteção de HP DESARMADA durante a ação atual.");
                PublishTelemetry(force: true);
            }
        }
    }

    public bool IsHealthy => _healthy;
    public bool HasPendingAlert => Volatile.Read(ref _pendingAlert) != 0;
    public long CapturedBufferCount => Interlocked.Read(ref _capturedBufferCount);
    public long AudibleBufferCount => Interlocked.Read(ref _audibleBufferCount);

    internal static string TestReference(string inputWavePath)
    {
        var prefixReferencePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Audio",
            "hp_alert_som2_prefix.wav");
        var fullReferencePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Audio",
            "hp_alert_som2_full.wav");
        var prefixMatcher = AudioEnvelopeMatcher.FromPcmWave(prefixReferencePath, PrefixDetectionThreshold);
        var fullMatcher = AudioEnvelopeMatcher.FromPcmWave(fullReferencePath, FullDetectionThreshold);
        using var reader = new WaveFileReader(inputWavePath);
        if (reader.WaveFormat.SampleRate != SampleRate ||
            reader.WaveFormat.BitsPerSample != BitsPerSample ||
            reader.WaveFormat.Channels != Channels)
        {
            throw new InvalidDataException("O áudio de teste deve ser PCM 16 kHz, 16-bit, mono.");
        }

        var buffer = new byte[2048];
        var prefixDetections = new List<double>();
        var fullDetections = new List<double>();
        double prefixBest = 0;
        double fullBest = 0;
        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            var prefixResult = prefixMatcher.AppendPcm16(buffer.AsSpan(0, read));
            var fullResult = fullMatcher.AppendPcm16(buffer.AsSpan(0, read));
            prefixBest = Math.Max(prefixBest, prefixResult.Confidence);
            fullBest = Math.Max(fullBest, fullResult.Confidence);
            if (prefixResult.Detected)
            {
                prefixDetections.Add(prefixResult.Confidence);
            }

            if (fullResult.Detected)
            {
                fullDetections.Add(fullResult.Confidence);
            }
        }

        return $"prefixDetections={prefixDetections.Count}; prefixBest={prefixBest:F4}; " +
               $"fullDetections={fullDetections.Count}; fullBest={fullBest:F4}; " +
               $"prefixScores={string.Join(',', prefixDetections.Select(value => value.ToString("F4")))}; " +
               $"fullScores={string.Join(',', fullDetections.Select(value => value.ToString("F4")))}";
    }

    public async Task StartAsync(int processId, CancellationToken cancellationToken)
    {
        if (_captureTask is not null)
        {
            if (!_captureTask.IsCompleted)
            {
                return;
            }

            // Uma captura que terminou por falha precisa ser completamente
            // liberada antes da reconexão deste mesmo cliente.
            await StopAsync();
        }

        var referencePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Audio",
            "hp_alert_som2_prefix.wav");
        var fullReferencePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Audio",
            "hp_alert_som2_full.wav");
        if (!File.Exists(referencePath))
        {
            throw new FileNotFoundException("A referência do alerta sonoro de HP baixo não foi encontrada.", referencePath);
        }

        if (!File.Exists(fullReferencePath))
        {
            throw new FileNotFoundException("A referência completa do alerta sonoro de HP baixo não foi encontrada.", fullReferencePath);
        }

        var prefixMatcher = AudioEnvelopeMatcher.FromPcmWave(referencePath, PrefixDetectionThreshold);
        var fullMatcher = AudioEnvelopeMatcher.FromPcmWave(fullReferencePath, FullDetectionThreshold);
        _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _captureTask = Task.Run(
            () => CaptureProcessAsync(processId, prefixMatcher, fullMatcher, ready, _captureCancellation.Token),
            CancellationToken.None);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
    }

    public bool TryConsumeAlert(out double confidence)
    {
        if (Interlocked.Exchange(ref _pendingAlert, 0) == 0)
        {
            confidence = 0;
            return false;
        }

        confidence = Volatile.Read(ref _pendingConfidence);
        return true;
    }

    public async Task StopAsync()
    {
        Armed = false;
        var cancellation = Interlocked.Exchange(ref _captureCancellation, null);
        var task = Interlocked.Exchange(ref _captureTask, null);
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            if (task is not null)
            {
                await task;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the bot is stopped.
        }
        catch (Exception exception)
        {
            // A falha já foi publicada pelo laço de captura. StopAsync deve
            // sempre liberar o estado para permitir uma nova tentativa.
            Status?.Invoke($"Captura de áudio anterior encerrada: {exception.GetBaseException().Message}");
        }
        finally
        {
            cancellation.Dispose();
            _healthy = false;
            PublishTelemetry(force: true);
        }
    }

    private async Task CaptureProcessAsync(
        int processId,
        AudioEnvelopeMatcher prefixMatcher,
        AudioEnvelopeMatcher fullMatcher,
        TaskCompletionSource ready,
        CancellationToken cancellationToken)
    {
        try
        {
            var format = new WaveFormat(SampleRate, BitsPerSample, Channels);
            await using var recorder = await new WasapiRecorderBuilder()
                .WithFormat(format)
                .WithProcessLoopback(
                    checked((uint)processId),
                    ProcessLoopbackMode.IncludeTargetProcessTree)
                .BuildAsync();

            _healthy = true;
            Status?.Invoke(
                $"Áudio seletivo conectado ao processo {processId}; detector do alerta de HP baixo ativo.");
            PublishTelemetry(force: true);
            ready.TrySetResult();

            await foreach (var buffer in recorder.CaptureAsync(cancellationToken))
            {
                if (buffer.Data.IsEmpty)
                {
                    continue;
                }

                Interlocked.Increment(ref _capturedBufferCount);
                var audioRms = ComputeRms(buffer.Data.Span);
                var prefixResult = prefixMatcher.AppendPcm16(buffer.Data.Span);
                var fullResult = fullMatcher.AppendPcm16(buffer.Data.Span);
                var confidence = Math.Max(prefixResult.Confidence, fullResult.Confidence);
                UpdateTelemetry(confidence, audioRms);
                if (!Armed)
                {
                    continue;
                }

                var nowTicks = DateTime.UtcNow.Ticks;
                var detected = prefixResult.Detected || fullResult.Detected;
                if (!detected && confidence >= NearDetectionThreshold)
                {
                    detected = RegisterNearCandidate(confidence, nowTicks);
                }

                if (!detected)
                {
                    continue;
                }

                var previousTicks = Interlocked.Read(ref _lastAlertTicks);
                if (previousTicks != 0 && new TimeSpan(nowTicks - previousTicks) < AlertCooldown)
                {
                    continue;
                }

                Interlocked.Exchange(ref _lastAlertTicks, nowTicks);
                Volatile.Write(ref _pendingConfidence, confidence);
                Interlocked.Exchange(ref _pendingAlert, 1);
                ResetNearCandidates();
                PublishTelemetry(force: true);
                Status?.Invoke($"Alerta sonoro de HP baixo CONFIRMADO ({confidence:P0}).");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ready.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            _healthy = false;
            ready.TrySetException(
                new InvalidOperationException(
                    "Não foi possível capturar seletivamente o áudio do Night Crows. " +
                    exception.GetBaseException().Message,
                    exception));
            Status?.Invoke($"Falha no áudio seletivo: {exception.GetBaseException().Message}");
        }
        finally
        {
            _healthy = false;
            PublishTelemetry(force: true);
        }
    }

    private bool RegisterNearCandidate(double confidence, long nowTicks)
    {
        var previousTicks = Interlocked.Read(ref _lastNearCandidateTicks);
        if (previousTicks != 0 && new TimeSpan(nowTicks - previousTicks) < NearCandidateSeparation)
        {
            return false;
        }

        var firstTicks = Interlocked.Read(ref _firstNearCandidateTicks);
        if (firstTicks == 0 || new TimeSpan(nowTicks - firstTicks) > NearCandidateWindow)
        {
            Interlocked.Exchange(ref _firstNearCandidateTicks, nowTicks);
            Interlocked.Exchange(ref _nearCandidateCount, 1);
        }
        else
        {
            Interlocked.Increment(ref _nearCandidateCount);
        }

        Interlocked.Exchange(ref _lastNearCandidateTicks, nowTicks);
        var count = Volatile.Read(ref _nearCandidateCount);
        Status?.Invoke($"Alerta sonoro quase confirmado ({confidence:P0}) — amostra {count}/2.");
        return count >= 2;
    }

    private void ResetNearCandidates()
    {
        Interlocked.Exchange(ref _nearCandidateCount, 0);
        Interlocked.Exchange(ref _firstNearCandidateTicks, 0);
        Interlocked.Exchange(ref _lastNearCandidateTicks, 0);
    }

    private static double ComputeRms(ReadOnlySpan<byte> pcm)
    {
        double sum = 0;
        var count = pcm.Length / 2;
        for (var index = 0; index < count; index++)
        {
            var offset = index * 2;
            var sample = (short)(pcm[offset] | (pcm[offset + 1] << 8));
            var normalized = sample / 32768d;
            sum += normalized * normalized;
        }

        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    private void UpdateTelemetry(double confidence, double audioRms)
    {
        lock (_telemetrySync)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            _currentAudioRms = audioRms;
            _peakAudioRms = Math.Max(_peakAudioRms, audioRms);
            if (audioRms >= AudibleRmsThreshold)
            {
                Interlocked.Increment(ref _audibleBufferCount);
                Interlocked.Exchange(ref _lastAudibleTicks, nowTicks);
            }

            if (_peakWindowStartedTicks == 0 || new TimeSpan(nowTicks - _peakWindowStartedTicks) >= PeakWindow)
            {
                _peakWindowStartedTicks = nowTicks;
                _recentPeakConfidence = confidence;
            }
            else
            {
                _recentPeakConfidence = Math.Max(_recentPeakConfidence, confidence);
            }

            _currentConfidence = confidence;
            var publishTelemetry = _lastTelemetryTicks == 0 ||
                                   new TimeSpan(nowTicks - _lastTelemetryTicks) >= TelemetryInterval;
            if (publishTelemetry)
            {
                _lastTelemetryTicks = nowTicks;
            }

            if (!publishTelemetry)
            {
                return;
            }
        }

        PublishTelemetry(force: false);
    }

    private void PublishTelemetry(bool force)
    {
        HpAudioMonitorSnapshot snapshot;
        lock (_telemetrySync)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            if (force)
            {
                _lastTelemetryTicks = nowTicks;
            }

            var alertTicks = Interlocked.Read(ref _lastAlertTicks);
            snapshot = new HpAudioMonitorSnapshot(
                IsHealthy,
                Armed,
                _currentConfidence,
                _recentPeakConfidence,
                alertTicks == 0 ? null : new DateTime(alertTicks, DateTimeKind.Utc).ToLocalTime(),
                CapturedBufferCount)
            {
                CurrentAudioRms = _currentAudioRms,
                PeakAudioRms = _peakAudioRms,
                AudibleBufferCount = AudibleBufferCount,
                HasRecentAudioSignal = _lastAudibleTicks != 0 &&
                                       new TimeSpan(nowTicks - _lastAudibleTicks) < AudioSignalRecentWindow
            };
        }

        Telemetry?.Invoke(snapshot);
    }
}

public sealed record HpAudioMonitorSnapshot(
    bool IsHealthy,
    bool IsArmed,
    double CurrentConfidence,
    double RecentPeakConfidence,
    DateTime? LastAlertAt,
    long CapturedBufferCount)
{
    public double CurrentAudioRms { get; init; }
    public double PeakAudioRms { get; init; }
    public long AudibleBufferCount { get; init; }
    public bool HasRecentAudioSignal { get; init; }
}

internal sealed class AudioEnvelopeMatcher
{
    private const int FrameSamples = 160;
    private readonly AudioFeature[] _reference;
    private readonly double _threshold;
    private readonly Queue<AudioFeature> _window;
    private readonly short[] _partialFrame = new short[FrameSamples];
    private int _partialCount;

    private AudioEnvelopeMatcher(AudioFeature[] reference, double threshold)
    {
        _reference = reference;
        _threshold = threshold;
        _window = new Queue<AudioFeature>(reference.Length + 1);
    }

    public static AudioEnvelopeMatcher FromPcmWave(string path, double threshold)
    {
        using var reader = new WaveFileReader(path);
        if (reader.WaveFormat.SampleRate != 16000 ||
            reader.WaveFormat.BitsPerSample != 16 ||
            reader.WaveFormat.Channels != 1)
        {
            throw new InvalidDataException("A referência do alerta deve ser PCM 16 kHz, 16-bit, mono.");
        }

        var bytes = new byte[checked((int)reader.Length)];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = reader.Read(bytes, total, bytes.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        var samples = new short[total / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
        var reference = BuildFeatures(samples);
        if (reference.Length < 30)
        {
            throw new InvalidDataException("A referência do alerta de HP ficou curta demais.");
        }

        return new AudioEnvelopeMatcher(reference, threshold);
    }

    public AudioMatchResult AppendPcm16(ReadOnlySpan<byte> pcm)
    {
        var best = 0d;
        for (var offset = 0; offset + 1 < pcm.Length; offset += 2)
        {
            _partialFrame[_partialCount++] = (short)(pcm[offset] | (pcm[offset + 1] << 8));
            if (_partialCount < FrameSamples)
            {
                continue;
            }

            _window.Enqueue(BuildFeature(_partialFrame));
            _partialCount = 0;
            while (_window.Count > _reference.Length)
            {
                _window.Dequeue();
            }

            if (_window.Count != _reference.Length)
            {
                continue;
            }

            var score = Compare(_reference, _window);
            best = Math.Max(best, score);
            if (score >= _threshold)
            {
                _window.Clear();
                return new AudioMatchResult(true, score);
            }
        }

        return new AudioMatchResult(false, best);
    }

    private static AudioFeature[] BuildFeatures(ReadOnlySpan<short> samples)
    {
        var count = samples.Length / FrameSamples;
        var result = new AudioFeature[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = BuildFeature(samples.Slice(index * FrameSamples, FrameSamples));
        }

        return result;
    }

    private static AudioFeature BuildFeature(ReadOnlySpan<short> samples)
    {
        double squareSum = 0;
        var crossings = 0;
        var previous = samples[0];
        for (var index = 0; index < samples.Length; index++)
        {
            var normalized = samples[index] / 32768d;
            squareSum += normalized * normalized;
            if (index > 0 && (samples[index] >= 0) != (previous >= 0))
            {
                crossings++;
            }

            previous = samples[index];
        }

        var rms = Math.Sqrt(squareSum / samples.Length);
        return new AudioFeature(
            Math.Log10(Math.Max(1e-7, rms)),
            crossings / (double)samples.Length);
    }

    private static double Compare(AudioFeature[] reference, IEnumerable<AudioFeature> candidateValues)
    {
        var candidate = candidateValues as AudioFeature[] ?? candidateValues.ToArray();
        var energy = Correlation(reference, candidate, static feature => feature.LogEnergy);
        var crossings = Correlation(reference, candidate, static feature => feature.ZeroCrossingRate);
        return Math.Clamp((energy * 0.82) + (crossings * 0.18), -1, 1);
    }

    private static double Correlation(
        IReadOnlyList<AudioFeature> first,
        IReadOnlyList<AudioFeature> second,
        Func<AudioFeature, double> selector)
    {
        var firstMean = first.Average(selector);
        var secondMean = second.Average(selector);
        double numerator = 0;
        double firstSquare = 0;
        double secondSquare = 0;
        for (var index = 0; index < first.Count; index++)
        {
            var firstValue = selector(first[index]) - firstMean;
            var secondValue = selector(second[index]) - secondMean;
            numerator += firstValue * secondValue;
            firstSquare += firstValue * firstValue;
            secondSquare += secondValue * secondValue;
        }

        var denominator = Math.Sqrt(firstSquare * secondSquare);
        return denominator <= 1e-12 ? 0 : numerator / denominator;
    }

    private readonly record struct AudioFeature(double LogEnergy, double ZeroCrossingRate);
}

internal readonly record struct AudioMatchResult(bool Detected, double Confidence);
