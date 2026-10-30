using System.Text.Json;
using System.Text.Json.Serialization;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Resources;
using Auros.Ram.Core.Services;

namespace Auros.Ram.Core.Serialization;

/// <summary>
/// RNF-63: exportación e importación del contenido completo en JSON.
/// El esquema está documentado en docs/formato-json.md y se versiona con <see cref="FormatVersion"/>.
/// </summary>
public static class JsonExchange
{
    public const string FormatId = "auros.ram/matrix";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Export(RiskMatrixDocument doc)
    {
        var p = doc.Project;
        var dto = new MatrixDto
        {
            Format = FormatId,
            FormatVersion = FormatVersion,
            ExportedUtc = DateTime.UtcNow,
            Project = new ProjectDto
            {
                Name = p.Name,
                Client = p.Client,
                Consultancy = p.Consultancy,
                Date = p.Date,
                Status = p.Status,
                ConsultancyLogo = p.ConsultancyLogo,
                ClientLogo = p.ClientLogo,
                RiskScaleId = p.RiskScaleId,
                ValueScaleId = p.ValueScaleId,
                DifficultyScaleId = p.DifficultyScaleId,
            },
            Scales = [.. doc.Scales.Select(s => new ScaleDto
            {
                Id = s.Id,
                Name = s.Name,
                Type = s.Type,
                Levels = [.. s.OrderedLevels.Select(l => new LevelDto { Id = l.Id, Name = l.Name, Color = l.Color, Order = l.Order })],
            })],
            Risks = [.. doc.Risks.Select(r => new RiskDto
            {
                Id = r.Id,
                Title = r.Title,
                ShortDescription = r.ShortDescription,
                LongDescription = r.LongDescription,
                RiskLevelId = r.RiskLevelId,
                ValueLevelId = r.ValueLevelId,
                DifficultyLevelId = r.DifficultyLevelId,
                RiskJustification = r.RiskJustification,
                ValueJustification = r.ValueJustification,
                DifficultyJustification = r.DifficultyJustification,
                CreatedUtc = r.CreatedUtc,
                ModifiedUtc = r.ModifiedUtc,
            })],
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Lee un JSON exportado y lo valida con las mismas reglas que la edición.</summary>
    public static RiskMatrixDocument Import(string json)
    {
        MatrixDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<MatrixDto>(json, Options);
        }
        catch (JsonException)
        {
            throw new DomainException(Messages.Error_JsonInvalid);
        }

        if (dto is null || dto.Format != FormatId)
        {
            throw new DomainException(Messages.Error_JsonInvalid);
        }

        if (dto.FormatVersion > FormatVersion)
        {
            throw new DomainException(Messages.Error_JsonNewerVersion);
        }

        var scales = (dto.Scales ?? []).Select(s => new Scale
        {
            Id = s.Id,
            Name = s.Name ?? "",
            Type = s.Type,
            Levels = [.. (s.Levels ?? []).Select(l => new Level { Id = l.Id, Name = l.Name ?? "", Color = Palette.Normalize(l.Color ?? ""), Order = l.Order })],
        }).ToList();
        foreach (var scale in scales)
        {
            Validation.ValidateScale(scale);
        }

        var p = dto.Project ?? new ProjectDto();
        var project = new ProjectInfo
        {
            Name = p.Name ?? "",
            Client = p.Client ?? "",
            Consultancy = p.Consultancy ?? "",
            Date = p.Date ?? DateOnly.FromDateTime(DateTime.Today),
            Status = p.Status,
            ConsultancyLogo = p.ConsultancyLogo,
            ClientLogo = p.ClientLogo,
            RiskScaleId = p.RiskScaleId,
            ValueScaleId = p.ValueScaleId,
            DifficultyScaleId = p.DifficultyScaleId,
        };

        var doc = new RiskMatrixDocument(project, scales, []);
        Validation.ValidateProject(project, doc);

        var risks = (dto.Risks ?? []).Select(r => new Risk
        {
            Id = r.Id,
            Title = r.Title ?? "",
            ShortDescription = r.ShortDescription ?? "",
            LongDescription = r.LongDescription ?? "",
            RiskLevelId = r.RiskLevelId,
            ValueLevelId = r.ValueLevelId,
            DifficultyLevelId = r.DifficultyLevelId,
            RiskJustification = r.RiskJustification ?? "",
            ValueJustification = r.ValueJustification ?? "",
            DifficultyJustification = r.DifficultyJustification ?? "",
            CreatedUtc = r.CreatedUtc ?? DateTime.UtcNow,
            ModifiedUtc = r.ModifiedUtc ?? DateTime.UtcNow,
        }).ToList();

        foreach (var risk in risks)
        {
            try
            {
                Validation.ValidateRisk(risk, doc);
            }
            catch (DomainException ex)
            {
                throw new DomainException(Validation.Format(Messages.Error_JsonRisk, risk.Title, ex.Message));
            }
        }

        return new RiskMatrixDocument(project, scales, risks);
    }

    // DTOs: el contrato del JSON es independiente del modelo interno.
    private sealed class MatrixDto
    {
        public string? Format { get; set; }
        public int FormatVersion { get; set; }
        public DateTime? ExportedUtc { get; set; }
        public ProjectDto? Project { get; set; }
        public List<ScaleDto>? Scales { get; set; }
        public List<RiskDto>? Risks { get; set; }
    }

    private sealed class ProjectDto
    {
        public string? Name { get; set; }
        public string? Client { get; set; }
        public string? Consultancy { get; set; }
        public DateOnly? Date { get; set; }
        public ProjectStatus Status { get; set; }
        public byte[]? ConsultancyLogo { get; set; }
        public byte[]? ClientLogo { get; set; }
        public Guid? RiskScaleId { get; set; }
        public Guid? ValueScaleId { get; set; }
        public Guid? DifficultyScaleId { get; set; }
    }

    private sealed class ScaleDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public ScaleType Type { get; set; }
        public List<LevelDto>? Levels { get; set; }
    }

    private sealed class LevelDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Color { get; set; }
        public int Order { get; set; }
    }

    private sealed class RiskDto
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? ShortDescription { get; set; }
        public string? LongDescription { get; set; }
        public Guid RiskLevelId { get; set; }
        public Guid ValueLevelId { get; set; }
        public Guid DifficultyLevelId { get; set; }
        public string? RiskJustification { get; set; }
        public string? ValueJustification { get; set; }
        public string? DifficultyJustification { get; set; }
        public DateTime? CreatedUtc { get; set; }
        public DateTime? ModifiedUtc { get; set; }
    }
}
