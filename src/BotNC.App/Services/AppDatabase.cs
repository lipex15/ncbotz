using System.IO;
using Microsoft.Data.Sqlite;
using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed class AppDatabase
{
    private readonly string _connectionString;

    public AppDatabase()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PEXBOT");
        Directory.CreateDirectory(dataDirectory);
        DatabasePath = Path.Combine(dataDirectory, "pexbot.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync()
    {
        MigrateLegacyDatabaseIfNeeded();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS visual_references (
                id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                file_name TEXT NOT NULL,
                image_blob BLOB NOT NULL,
                source_x INTEGER NOT NULL,
                source_y INTEGER NOT NULL,
                source_width INTEGER NOT NULL,
                source_height INTEGER NOT NULL,
                search_x INTEGER NOT NULL,
                search_y INTEGER NOT NULL,
                search_width INTEGER NOT NULL,
                search_height INTEGER NOT NULL,
                threshold REAL NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS bot_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();

        foreach (var definition in ReferenceDefinitions.All)
        {
            await UpsertReferenceAsync(connection, definition);
        }
    }

    private void MigrateLegacyDatabaseIfNeeded()
    {
        if (File.Exists(DatabasePath))
        {
            return;
        }

        var previousPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BotNC by LIPEX",
            "botnc.db");
        if (!File.Exists(previousPath))
        {
            return;
        }

        // SQLite backup reads a consistent snapshot even when the previous app used WAL mode.
        using var previous = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = previousPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        using var migrated = new SqliteConnection(_connectionString);
        previous.Open();
        migrated.Open();
        previous.BackupDatabase(migrated);
    }

    public async Task<VisualReference> GetReferenceAsync(string id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, display_name, image_blob,
                   source_x, source_y, source_width, source_height,
                   search_x, search_y, search_width, search_height, threshold
            FROM visual_references
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Referência visual não cadastrada: {id}.");
        }

        return new VisualReference(
            reader.GetString(0),
            reader.GetString(1),
            (byte[])reader[2],
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetDouble(11));
    }

    public async Task SaveSettingAsync(string key, string value)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO bot_settings(key, value) VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM bot_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task UpsertReferenceAsync(
        SqliteConnection connection,
        ReferenceDefinition definition)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "References",
            definition.FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Imagem de referência ausente: {definition.FileName}",
                path);
        }

        var image = await File.ReadAllBytesAsync(path);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO visual_references(
                id, display_name, file_name, image_blob,
                source_x, source_y, source_width, source_height,
                search_x, search_y, search_width, search_height,
                threshold, updated_at)
            VALUES(
                $id, $displayName, $fileName, $image,
                $sourceX, $sourceY, $sourceWidth, $sourceHeight,
                $searchX, $searchY, $searchWidth, $searchHeight,
                $threshold, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                file_name = excluded.file_name,
                image_blob = excluded.image_blob,
                source_x = excluded.source_x,
                source_y = excluded.source_y,
                source_width = excluded.source_width,
                source_height = excluded.source_height,
                search_x = excluded.search_x,
                search_y = excluded.search_y,
                search_width = excluded.search_width,
                search_height = excluded.search_height,
                threshold = excluded.threshold,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", definition.Id);
        command.Parameters.AddWithValue("$displayName", definition.DisplayName);
        command.Parameters.AddWithValue("$fileName", definition.FileName);
        command.Parameters.AddWithValue("$image", image);
        command.Parameters.AddWithValue("$sourceX", definition.SourceX);
        command.Parameters.AddWithValue("$sourceY", definition.SourceY);
        command.Parameters.AddWithValue("$sourceWidth", definition.SourceWidth);
        command.Parameters.AddWithValue("$sourceHeight", definition.SourceHeight);
        command.Parameters.AddWithValue("$searchX", definition.SearchX);
        command.Parameters.AddWithValue("$searchY", definition.SearchY);
        command.Parameters.AddWithValue("$searchWidth", definition.SearchWidth);
        command.Parameters.AddWithValue("$searchHeight", definition.SearchHeight);
        command.Parameters.AddWithValue("$threshold", definition.Threshold);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private sealed record ReferenceDefinition(
        string Id,
        string DisplayName,
        string FileName,
        int SourceX,
        int SourceY,
        int SourceWidth,
        int SourceHeight,
        int SearchX,
        int SearchY,
        int SearchWidth,
        int SearchHeight,
        double Threshold);

    private static class ReferenceDefinitions
    {
        public static readonly ReferenceDefinition[] All =
        [
            new(
                "menu_masmorra",
                "Botão Masm. no menu lateral",
                "menu_masmorra.png",
                0, 0, 75, 82,
                1580, 190, 300, 260,
                0.72),
            new(
                "tela_masmorras",
                "Título da página Masmorra",
                "tela_masmorras.png",
                25, 35, 270, 80,
                0, 20, 380, 130,
                0.72),
            new(
                "confirmar_sepheras",
                "Confirmação Ruínas de Sepheras",
                "confirmar_sepheras.png",
                700, 345, 525, 375,
                590, 260, 750, 540,
                0.68),
            new(
                "atalaia_erodida",
                "Indicador Atalaia Erodida",
                "atalaia_erodida_indicador.png",
                10, 58, 310, 52,
                0, 45, 420, 105,
                0.63),
            new(
                "caca_automatica",
                "Caça automática em uso",
                "caca_automatica.png",
                140, 55, 380, 100,
                680, 730, 620, 240,
                0.63),
            new(
                "tela_descanso",
                "Tela de descanso genérica",
                "descanso_generico.png",
                760, 140, 400, 50,
                650, 100, 650, 120,
                0.68),
            new(
                "descanso_sepheras",
                "Descanso dentro de Sapheras",
                "descanso_sepheras.png",
                15, 45, 520, 80,
                0, 25, 650, 135,
                0.62),
            new(
                "menu_ta",
                "Botão T.A. no menu lateral",
                "menu_ta.png",
                0, 0, 94, 96,
                1660, 350, 180, 180,
                0.67),
            new(
                "seletor_ta",
                "Piloto da Terra Avassaladora",
                "seletor_ta_tela.png",
                30, 25, 540, 75,
                0, 10, 680, 125,
                0.65),
            new(
                "entrar_ta2_pronto",
                "Botão Entrar da T.A 2 pronto",
                "seletor_ta_tela.png",
                725, 754, 82, 30,
                710, 740, 130, 60,
                0.68),
            new(
                "entrar_ta3_pronto",
                "Botão Entrar da T.A 3 pronto",
                "seletor_ta_tela.png",
                725, 754, 82, 30,
                980, 740, 130, 60,
                0.68),
            new(
                "ta3_chegada",
                "Serviços da T.A 3",
                "ta3_chegada.png",
                10, 90, 315, 165,
                0, 70, 410, 230,
                0.64),
            new(
                "ta2_chegada",
                "Serviços da T.A 2",
                "ta2_chegada.png",
                10, 90, 315, 165,
                0, 70, 410, 230,
                0.61),
            new(
                "ta3_artigos",
                "Painel Artigos da T.A 3",
                "ta3_artigos.png",
                0, 0, 230, 70,
                0, 75, 350, 125,
                0.65),
            new(
                "loja_artigos",
                "Mercador de Artigos",
                "loja_artigos.png",
                20, 25, 455, 75,
                0, 10, 620, 130,
                0.67),
            new(
                "compra_concluida",
                "Item Obtido",
                "compra_concluida_v2.png",
                790, 395, 340, 80,
                650, 339, 620, 180,
                0.66),
            new(
                "mapa_ta3",
                "Mapa da Zona Vulcânica de Auvers",
                "mapa_ta3.png",
                20, 25, 390, 125,
                0, 10, 540, 180,
                0.65),
            new(
                "mapa_aberto",
                "Mapa aberto",
                "mapa_ta2_favorito_unico.png",
                24, 25, 175, 70,
                0, 10, 280, 120,
                0.64),
            new(
                "mapa_aberto_ta2_ir",
                "Mapa aberto da T.A 2 com seletor Ir",
                "botao_ir_ta2_tela.png",
                20, 25, 180, 70,
                0, 10, 300, 125,
                0.60),
            new(
                "aba_favoritos",
                "Aba Favoritos",
                "mapa_posto_favoritos.png",
                1720, 145, 190, 75,
                1660, 105, 260, 130,
                0.62),
            new(
                "segundo_favorito_teleporte",
                "Segundo favorito de teleporte",
                "mapa_posto_favoritos.png",
                1550, 330, 105, 85,
                1525, 310, 150, 125,
                0.66),
            new(
                "mapa_favoritos",
                "Favoritos da T.A 3",
                "mapa_favoritos.png",
                90, 95, 395, 350,
                1420, 75, 500, 440,
                0.61),
            new(
                "posto_patrulha_sul",
                "Posto de Patrulha Sul",
                "posto_patrulha_sul.png",
                10, 90, 310, 55,
                0, 70, 420, 105,
                0.63),
            new(
                "mapa_posto_informacoes",
                "Mapa do Posto de Patrulha Sul",
                "mapa_posto_informacoes.png",
                1510, 140, 395, 120,
                1450, 100, 470, 200,
                0.63),
            new(
                "mapa_terra_fogo_silex",
                "Terra do Fogo de Sílex",
                "mapa_terra_fogo_silex.png",
                15, 88, 390, 65,
                1, 70, 500, 105,
                0.64),
            new(
                "mapa_posto_favoritos",
                "Mapa do Posto de Patrulha Sul",
                "mapa_posto_favoritos.png",
                850, 430, 300, 200,
                800, 400, 420, 300,
                0.62),
            new(
                "botao_ir",
                "Botão Ir do mapa",
                "botao_ir_tela_v2.png",
                1395, 645, 80, 50,
                799, 249, 901, 601,
                0.58),
            new(
                "botao_ir_legado",
                "Botão Ir do mapa — aparência alternativa",
                "teste_botao_ir.png",
                1001, 497, 80, 50,
                799, 249, 901, 601,
                0.58),
            new(
                "botao_ir_ta2",
                "Botão Ir do mapa — T.A 2",
                "botao_ir_ta2_tela.png",
                622, 128, 60, 50,
                350, 50, 750, 500,
                0.56),
            new(
                "descanso_ponto_fixo",
                "Aguardando no ponto fixo",
                "descanso_ponto_fixo.png",
                775, 810, 390, 55,
                649, 758, 661, 146,
                0.60),
            new(
                "descanso_movendo",
                "Movendo-se",
                "descanso_movendo.png",
                800, 810, 330, 115,
                650, 720, 660, 240,
                0.63),
            new(
                "descanso_aguardando_spot",
                "Aguardando no spot",
                "descanso_aguardando_spot.png",
                800, 810, 330, 115,
                650, 720, 660, 240,
                0.64),
            new(
                "morte_confirmada",
                "Tela Você morreu",
                "morte_confirmada.png",
                720, 125, 500, 165,
                610, 80, 720, 270,
                0.63),
            new(
                "morte_confirmada_ta2",
                "Tela Você morreu — Cliente 2",
                "morte_confirmada_ta2.png",
                720, 125, 500, 165,
                610, 80, 720, 270,
                0.62),
            new(
                "morte_titulo",
                "Título Você morreu em qualquer mapa",
                "morte_confirmada.png",
                785, 155, 350, 70,
                765, 135, 390, 110,
                0.70),
            new(
                "morte_ressuscitar",
                "Botão Ressuscitar da tela de morte",
                "morte_confirmada.png",
                1692, 968, 195, 50,
                1670, 950, 230, 78,
                0.72),
            new(
                "descanso_morte",
                "Tela de descanso com estado Morte",
                "descanso_morte_ta2.png",
                850, 825, 220, 90,
                700, 740, 700, 230,
                0.62),
            new(
                "menu_agenda",
                "Botão Agenda no menu lateral",
                "menu_agenda.png",
                0, 0, 80, 94,
                1680, 430, 190, 200,
                0.60),
            new(
                "perda_exp",
                "Painel Perda de EXP",
                "perda_exp.png",
                75, 90, 355, 180,
                0, 55, 520, 280,
                0.61),
            new(
                "painel_restauracao",
                "Painel lateral de restauração de recursos",
                "perda_exp.png",
                15, 82, 62, 72,
                0, 50, 135, 150,
                0.66),
            new(
                "icone_perda_exp",
                "Ícone vermelho de restauração de morte",
                "perda_exp.png",
                1502, 18, 70, 82,
                1370, 0, 430, 165,
                0.59),
            new(
                "agenda_tela",
                "Tela da Agenda",
                "agenda_tela.png",
                20, 20, 300, 115,
                0, 0, 430, 170,
                0.64),
            new(
                "aviso_agenda",
                "Aviso de agenda indisponível",
                "aviso_agenda.png",
                760, 455, 400, 85,
                600, 329, 720, 300,
                0.70)
        ];
    }
}
