using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private async Task ServicePartiesAsync(IReadOnlyList<ClientSession> sessions, PauseController pause, CancellationToken token)
    {
        if (HumanOwnsInterface || HasPendingProtection) return;
        foreach (var session in sessions)
        {
            var options = session.Options.Party;
            if (options is null || options.Role == PartyRole.Disabled || session.ReconnectPending ||
                !session.StartupRestorationChecked || session.NeedsDeathRestoration || session.HandlingDeath ||
                session.HandlingProtection || session.InDailyCampaign || session.LoveBossInside || session.InAgenda ||
                DateTime.UtcNow < session.Party.NextScan) continue;
            var state = session.Party;
            state.Reconnected(session.DisconnectedAt);
            // Once established, no screenshots, OCR or P-panel polling for party.
            // A reconnect (or a new ClientSession) explicitly re-arms this work.
            if (!state.NeedsObservation) continue;
            state.NextScan = DateTime.UtcNow.AddSeconds(3);
            var previousInterruptible = _interruptibleAction.Value;
            _interruptibleAction.Value = session;
            try
            {
                BindWorkflowClient(session);
                var frame = await CaptureClientFrameAsync(session, token);
                if (options.Role == PartyRole.Receiver)
                {
                    if (await PartyReader.HasMemberCardAsync(recognition, frame, token))
                    {
                        state.AcceptanceAwaitingCard = false;
                        state.RosterEstablished = true;
                        state.RecheckAfterReconnect = false;
                        WriteLog(session, "Grupo: entrada confirmada pelo card do integrante.");
                        continue;
                    }
                    if (await PartyReader.HasInvitationAsync(recognition, frame, token))
                        await AcceptPartyInvitationAsync(session, options, pause, token);
                    continue;
                }
                if (DateTime.UtcNow < state.NextLeaderCheck ||
                    !(session.IsFarmingTa || session.SapherasFarmConfirmed)) continue;
                state.NextLeaderCheck = DateTime.UtcNow.AddSeconds(60);
                await MaintainPartyLeaderAsync(session, options, pause, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                // Optional social actions must never reset the farm route or
                // create a restoration/protection failure on either client.
                state.NextScan = DateTime.UtcNow.AddSeconds(5);
                WritePersistentOnly(session, $"party_retry error={error.GetType().Name}; detail={error.Message}; farmRouteUnchanged=true");
            }
            finally { _interruptibleAction.Value = previousInterruptible; }
        }
    }

    private async Task PartyClickAsync(ClientSession session, int x, int y, CancellationToken token)
    {
        await EnsureGameForegroundAsync(session, token);
        var point = gameWindows.MapReferencePoint(session.Options.Target, x, y);
        await input.MoveAndClickAsync(point.X, point.Y, TimeSpan.FromMilliseconds(160), token,
            cooldown: TimeSpan.FromMilliseconds(250));
    }

    private async Task AcceptPartyInvitationAsync(ClientSession session, PartyOptions options, PauseController pause, CancellationToken token)
    {
        var hadRest = await FindRestStateAsync(session, token) is not null;
        if (hadRest) await ExitRestIfNeededAsync(session, pause, token);
        try
        {
            var frame = await CaptureClientFrameAsync(session, token);
            if (!await PartyReader.HasInvitationAsync(recognition, frame, token)) return;
            var text = await PartyReader.TextAsync(frame, 40, 76, 345, 65, token);
            if (PartyPolicy.ClearlyDifferentInviter(text, options.PreferredInviter))
            {
                await Task.Delay(200, token);
                var second = await PartyReader.TextAsync(await CaptureClientFrameAsync(session, token), 40, 76, 345, 65, token);
                if (second == text)
                {
                    WriteLog(session, "Grupo: convite de outro remetente legível ignorado.");
                    return;
                }
            }
            // Names with uncommon symbols often cannot be transcribed by the
            // installed OCR language. The user explicitly allows acceptance.
            WritePersistentOnly(session, $"party_invite_received preferredMatch={PartyPolicy.ContainsName(text, options.PreferredInviter)}; ambiguousNameAllowed=true; text={text}");
            await PartyClickAsync(session, 328, 240, token);
            await Task.Delay(400, token);
            frame = await CaptureClientFrameAsync(session, token);
            var card = await PartyReader.HasMemberCardAsync(recognition, frame, token);
            session.Party.AcceptanceAwaitingCard = !card;
            session.Party.RosterEstablished = card;
            if (card) session.Party.RecheckAfterReconnect = false;
            WriteLog(session, card ? "Grupo: entrada confirmada pelo card do integrante." :
                "Grupo: OK do convite enviado; aguardando confirmação visual do integrante, sem parar o farm.");
            session.Party.NextScan = DateTime.UtcNow.AddSeconds(5);
        }
        finally { await FinishPartyUiAsync(session, hadRest, pause, token); }
    }

    private async Task MaintainPartyLeaderAsync(ClientSession session, PartyOptions options, PauseController pause, CancellationToken token)
    {
        if (options.InviteNames.Count == 0) return;
        var state = session.Party;
        var hadRest = await FindRestStateAsync(session, token) is not null;
        if (hadRest) await ExitRestIfNeededAsync(session, pause, token);
        try
        {
            var frame = await CaptureClientFrameAsync(session, token);
            if (!(await recognition.FindAsync("party_panel", frame, token)).Found)
            {
                await input.PressKeyAsync(0x50, cancellationToken: token);
                if (!await WaitForReferenceToAppearAsync("party_panel", TimeSpan.FromSeconds(3), pause, token)) return;
                frame = await CaptureClientFrameAsync(session, token);
            }
            if ((await recognition.FindAsync("party_empty", frame, token)).Found)
            {
                state.Confirmed.Clear();
                state.AwaitingName = null;
                await PartyClickAsync(session, 228, 770, token);
                if (!await WaitForReferenceToAppearAsync("party_create_confirm", TimeSpan.FromSeconds(3), pause, token)) return;
                await input.PressKeyAsync(KeyY, cancellationToken: token);
                if (!await WaitForReferenceToAppearAsync("party_dissolve", TimeSpan.FromSeconds(3), pause, token)) return;
                frame = await CaptureClientFrameAsync(session, token);
                WriteLog(session, "Grupo: equipe criada; preparando os convites configurados.");
            }
            var leader = await recognition.FindAsync("party_dissolve", frame, token);
            var rows = PartyReader.MemberRows(frame);
            var names = new List<string>();
            foreach (var row in rows)
                names.Add(await PartyReader.TextAsync(frame, 100, Math.Max(180, row - 8), 205, 37, token));
            var seen = options.InviteNames.Where(n => names.Any(text => PartyPolicy.ContainsName(text, n))).ToHashSet(StringComparer.Ordinal);
            if (state.AwaitingName is { } invited && rows.Length > state.MembersBeforeInvite)
            {
                state.Confirmed.Add(invited);
                WriteLog(session, $"Grupo: novo integrante observado após o convite para {invited}.");
            }
            state.AwaitingName = null;
            // A smaller roster invalidates stale remembered members.
            if (state.Confirmed.Count > Math.Max(0, rows.Length - 1)) state.Confirmed.Clear();
            state.Confirmed.UnionWith(seen);
            WritePersistentOnly(session, $"party_roster leader={leader.Found}; members={rows.Length}; matched={seen.Count}; remembered={state.Confirmed.Count}; reconnect={state.RecheckAfterReconnect}; rows={string.Join(" | ", names)}");
            if (options.InviteNames.All(state.Confirmed.Contains))
            {
                state.RosterEstablished = true;
                state.RecheckAfterReconnect = false;
                state.UncertainReads = 0;
                state.NextLeaderCheck = DateTime.UtcNow.AddMinutes(3);
                WritePersistentOnly(session, "party_existing=preserved; allConfiguredMembers=true; invite=false; dissolve=false");
                return;
            }
            if (!leader.Found)
            {
                WriteLog(session, "Grupo: este personagem não tem o botão Dissolver; preservando a equipe, sem assumir liderança.");
                return;
            }
            var enoughMembers = rows.Length >= options.InviteNames.Count + 1;
            if (enoughMembers && !state.RecheckAfterReconnect)
            {
                state.RosterEstablished = true;
                // Do not destroy a pre-existing party solely because Unicode
                // OCR cannot read the member's name at startup.
                state.NextLeaderCheck = DateTime.UtcNow.AddMinutes(3);
                WritePersistentOnly(session, "party_existing=preserved; reason=roster_full_names_partially_unreadable; dissolve=false");
                return;
            }
            if (state.RecheckAfterReconnect && enoughMembers && ++state.UncertainReads >= 2 &&
                !state.RebuiltAfterReconnect && DateTime.UtcNow - state.LastRebuild >= TimeSpan.FromMinutes(5) &&
                options.InviteNames.All(n => state.Attempts.GetValueOrDefault(n) < 5))
            {
                // Locate by visual result: Dissolver can move vertically.
                await PartyClickAsync(session, leader.X, leader.Y, token);
                if (!await WaitForReferenceToAppearAsync("party_dissolve_confirm", TimeSpan.FromSeconds(3), pause, token)) return;
                await input.PressKeyAsync(KeyY, cancellationToken: token);
                await WaitForReferenceToDisappearAsync("party_dissolve_confirm", TimeSpan.FromSeconds(3), pause, token);
                var dissolvedFrame = await CaptureClientFrameAsync(session, token);
                if ((await recognition.FindAsync("party_panel", dissolvedFrame, token)).Found)
                {
                    WriteLog(session, "Grupo: dissolução ainda não confirmada; preservando estado e retomando o farm.");
                    return;
                }
                state.RebuiltAfterReconnect = true;
                state.RecheckAfterReconnect = false;
                state.LastRebuild = DateTime.UtcNow;
                state.Confirmed.Clear();
                state.NextLeaderCheck = DateTime.UtcNow.AddSeconds(3);
                WriteLog(session, "Grupo: líder confirmou dissolução após reconexão; recriação será feita na próxima ação, sem zerar tentativas.");
                return; // P closes itself; never press P here to 'close' it again.
            }
            if (enoughMembers) return;
            var next = Enumerable.Range(0, options.InviteNames.Count)
                .Select(offset => options.InviteNames[(state.NextInviteIndex + offset) % options.InviteNames.Count])
                .FirstOrDefault(state.MayInvite);
            if (next is null)
            {
                state.NextLeaderCheck = DateTime.UtcNow.AddMinutes(3);
                WritePersistentOnly(session, "party_invites=suspended; reason=confirmed_or_five_attempts; reset=stop_and_start_only");
                return;
            }
            await PartyClickAsync(session, 148, 770, token);
            if (!await WaitForReferenceToAppearAsync("party_invite_prompt", TimeSpan.FromSeconds(3), pause, token)) return;
            await PartyClickAsync(session, 950, 551, token);
            await input.SelectAllTextAsync(token);
            await input.TypeUnicodeTextAsync(next, token);
            frame = await CaptureClientFrameAsync(session, token);
            if (!(await recognition.FindAsync("party_invite_prompt", frame, token)).Found) return;
            // Reserve before sending: interruption must never exceed five sends.
            var attempt = state.RecordInvite(next);
            state.NextInviteIndex = (options.InviteNames.ToList().IndexOf(next) + 1) % options.InviteNames.Count;
            await PartyClickAsync(session, 1075, 638, token); // Never Y in the text field.
            state.AwaitingName = next;
            state.MembersBeforeInvite = rows.Length;
            state.NextLeaderCheck = DateTime.UtcNow.AddSeconds(45);
            WriteLog(session, $"Grupo: convite para {next} enviado ({attempt}/5); farm continua enquanto aguarda.");
        }
        finally { await FinishPartyUiAsync(session, hadRest, pause, token); }
    }

    private async Task FinishPartyUiAsync(ClientSession session, bool hadRest, PauseController pause, CancellationToken token)
    {
        if (token.IsCancellationRequested || session.ReconnectPending || HasPendingProtection || HumanOwnsInterface) return;
        var frame = await CaptureClientFrameAsync(session, token);
        if ((await recognition.FindAsync("party_invite_prompt", frame, token)).Found ||
            (await recognition.FindAsync("party_create_confirm", frame, token)).Found ||
            (await recognition.FindAsync("party_dissolve_confirm", frame, token)).Found)
        {
            await input.PressKeyAsync(KeyEscape, cancellationToken: token);
            frame = await CaptureClientFrameAsync(session, token);
        }
        if ((await recognition.FindAsync("party_panel", frame, token)).Found)
            await PartyClickAsync(session, 382, 123, token);
        if (hadRest && session.Options.KeepRestMode) session.SafeInRest = await TryOpenRestPanelAsync(session, pause, token) is not null;
    }
}
