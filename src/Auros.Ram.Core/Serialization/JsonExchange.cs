using System.Text.Json;
using System.Text.Json.Serialization;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Resources;
using Auros.Ram.Core.Services;

namespace Auros.Ram.Core.Serialization;

/// <summary>
/// RNF-63: exportación e importación del contenido completo en JSON.
/// El esquema está documentado en docs/formato-json.md y se versiona con <see cref="FormatVersion"/>.
/// Versión 3: riesgos clasificados por impacto y probabilidad, con mapa de calor. Versión 4: historial de
/// evaluaciones de cada riesgo (un JSON de la versión 3 se importa con una sola evaluación por riesgo).
/// Versión 5: rangos de los niveles (monto o porcentaje) y monto del impacto y probabilidad en porcentaje.
/// Versión 6: catálogos de consecuencias y de criterios de materialidad, y su uso en cada riesgo (un JSON anterior
/// se importa con los catálogos sugeridos). Versión 7: varias empresas con sus unidades de negocio, y la empresa y
/// unidad de cada riesgo (un JSON anterior se importa con la empresa del proyecto y la unidad "General").
/// Las versiones 1 y 2 (valor agregado y dificultad) fueron de prueba y ya no se importan.
/// </summary>
public static class JsonExchange
{
    public const string FormatId = "auros.ram/matrix";
    public const int FormatVersion = 7;
    public const int OldestSupportedVersion = 3;

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
                Company = p.Company,
                Date = p.Date,
                Status = p.Status,
                CompanyLogo = p.CompanyLogo,
                RiskScaleId = p.RiskScaleId,
                ImpactScaleId = p.ImpactScaleId,
                ProbabilityScaleId = p.ProbabilityScaleId,
                Companies = [.. p.Companies.Select(c => new CompanyDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Logo = c.Logo,
                    Units = [.. c.Units.Select(u => new CatalogItemDto { Id = u.Id, Name = u.Name })],
                })],
                Consequences = [.. p.Consequences.Select(i => new CatalogItemDto { Id = i.Id, Name = i.Name })],
                MaterialityCriteria = [.. p.MaterialityCriteria.Select(i => new CatalogItemDto { Id = i.Id, Name = i.Name })],
                HeatMap = [.. p.HeatMap.Select(c => new HeatCellDto
                {
                    ImpactLevelId = c.ImpactLevelId,
                    ProbabilityLevelId = c.ProbabilityLevelId,
                    RiskLevelId = c.RiskLevelId,
                })],
            },
            Scales = [.. doc.Scales.Select(s => new ScaleDto
            {
                Id = s.Id,
                Name = s.Name,
                Type = s.Type,
                Levels = [.. s.OrderedLevels.Select(l => new LevelDto
                {
                    Id = l.Id, Name = l.Name, Color = l.Color, TextColor = l.TextColor, Order = l.Order, RangeFrom = l.RangeFrom, RangeTo = l.RangeTo,
                })],
            })],
            Risks = [.. doc.Risks.Select(r => new RiskDto
            {
                Id = r.Id,
                Title = r.Title,
                ShortDescription = r.ShortDescription,
                LongDescription = r.LongDescription,
                ImpactLevelId = r.ImpactLevelId,
                ProbabilityLevelId = r.ProbabilityLevelId,
                ImpactJustification = r.ImpactJustification,
                ProbabilityJustification = r.ProbabilityJustification,
                CreatedUtc = r.CreatedUtc,
                ModifiedUtc = r.ModifiedUtc,
                ImpactAmount = r.ImpactAmount,
                ProbabilityPercent = r.ProbabilityPercent,
                CompanyId = r.CompanyId == Guid.Empty ? null : r.CompanyId,
                UnitId = r.UnitId == Guid.Empty ? null : r.UnitId,
                ConsequenceIds = [.. r.ConsequenceIds],
                MaterialityId = r.MaterialityId,
                Assessments = [.. r.Assessments.Select(a => new AssessmentDto
                {
                    Id = a.Id,
                    Date = a.Date,
                    ImpactLevelId = a.ImpactLevelId,
                    ProbabilityLevelId = a.ProbabilityLevelId,
                    Note = a.Note,
                    RecordedUtc = a.RecordedUtc,
                    ImpactAmount = a.ImpactAmount,
                    ProbabilityPercent = a.ProbabilityPercent,
                })],
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

        if (dto.FormatVersion < OldestSupportedVersion)
        {
            throw new DomainException(Messages.Error_JsonObsoleteVersion);
        }

        var scales = (dto.Scales ?? []).Select(s => new Scale
        {
            Id = s.Id,
            Name = s.Name ?? "",
            Type = s.Type,
            Levels = [.. (s.Levels ?? []).Select(l =>
            {
                var color = Palette.Normalize(l.Color ?? "");
                var text = l.TextColor is null ? Palette.ContrastingText(color) : Palette.Normalize(l.TextColor);
                return new Level { Id = l.Id, Name = l.Name ?? "", Color = color, TextColor = text, Order = l.Order, RangeFrom = l.RangeFrom, RangeTo = l.RangeTo };
            })],
        }).ToList();
        foreach (var scale in scales)
        {
            Validation.ValidateScale(scale);
        }

        var p = dto.Project ?? new ProjectDto();
        var project = new ProjectInfo
        {
            Name = p.Name ?? "",
            Company = p.Company ?? "",
            Date = p.Date ?? DateOnly.FromDateTime(DateTime.Today),
            Status = p.Status,
            CompanyLogo = p.CompanyLogo,
            RiskScaleId = p.RiskScaleId,
            ImpactScaleId = p.ImpactScaleId,
            ProbabilityScaleId = p.ProbabilityScaleId,
        };

        // Catálogos: los del JSON; uno anterior a la versión 6 recibe los sugeridos.
        if (p.Consequences is null && p.MaterialityCriteria is null)
        {
            project.Consequences = RiskMatrixDocument.SuggestedCatalog(CatalogKind.Consequence);
            project.MaterialityCriteria = RiskMatrixDocument.SuggestedCatalog(CatalogKind.Materiality);
        }
        else
        {
            project.Consequences = Catalog(p.Consequences);
            project.MaterialityCriteria = Catalog(p.MaterialityCriteria);
        }

        // Empresas: las del JSON; uno anterior a la versión 7 recibe la del proyecto con la unidad "General".
        project.Companies = p.Companies is { Count: > 0 } companies
            ? [.. companies.Select(c =>
            {
                Validation.ValidateCatalogName((c.Name ?? "").Trim());
                return new Company(c.Id == Guid.Empty ? Guid.NewGuid() : c.Id, (c.Name ?? "").Trim(), c.Logo, Catalog(c.Units) is { Count: > 0 } u
                    ? u.Select(i => new BusinessUnit(i.Id, i.Name)).ToList()
                    : [new BusinessUnit(Guid.NewGuid(), Messages.Default_Unit)]);
            })]
            : [RiskMatrixDocument.DefaultCompany(project)];

        var doc = new RiskMatrixDocument(project, scales, []);
        Validation.ValidateProject(project, doc);
        project.HeatMap = HeatMap.Prune(
            [.. (p.HeatMap ?? []).Select(c => new HeatCell(c.ImpactLevelId, c.ProbabilityLevelId, c.RiskLevelId))],
            doc.ProjectScale(ScaleType.Risk), doc.ProjectScale(ScaleType.Impact), doc.ProjectScale(ScaleType.Probability));

        var risks = (dto.Risks ?? []).Select(r => WithHistory(r, new Risk
        {
            Id = r.Id,
            Title = r.Title ?? "",
            ShortDescription = r.ShortDescription ?? "",
            LongDescription = r.LongDescription ?? "",
            ImpactLevelId = r.ImpactLevelId,
            ProbabilityLevelId = r.ProbabilityLevelId,
            ImpactJustification = r.ImpactJustification ?? "",
            ProbabilityJustification = r.ProbabilityJustification ?? "",
            CreatedUtc = r.CreatedUtc ?? DateTime.UtcNow,
            ModifiedUtc = r.ModifiedUtc ?? DateTime.UtcNow,
            ImpactAmount = r.ImpactAmount,
            ProbabilityPercent = r.ProbabilityPercent,
            CompanyId = r.CompanyId ?? project.Companies[0].Id,
            UnitId = r.UnitId ?? (project.FindCompany(r.CompanyId ?? project.Companies[0].Id)?.Units.FirstOrDefault()?.Id ?? Guid.Empty),
            ConsequenceIds = [.. (r.ConsequenceIds ?? []).Distinct()],
            MaterialityId = r.MaterialityId,
        })).ToList();

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

    /// <summary>
    /// El historial del riesgo; sin historial (JSON de la versión 3), una sola evaluación con su estado actual
    /// en la fecha en que se creó.
    /// </summary>
    private static Risk WithHistory(RiskDto dto, Risk risk)
    {
        risk.Assessments = dto.Assessments is { Count: > 0 } list
            ? [.. list.Select(a => new RiskAssessment(
                a.Id == Guid.Empty ? Guid.NewGuid() : a.Id, a.Date, a.ImpactLevelId, a.ProbabilityLevelId,
                a.Note ?? "", a.RecordedUtc ?? risk.ModifiedUtc)
                { ImpactAmount = a.ImpactAmount, ProbabilityPercent = a.ProbabilityPercent })]
            : [new RiskAssessment(Guid.NewGuid(), DateOnly.FromDateTime(risk.CreatedUtc.ToLocalTime()),
                risk.ImpactLevelId, risk.ProbabilityLevelId, "", risk.CreatedUtc)
                { ImpactAmount = risk.ImpactAmount, ProbabilityPercent = risk.ProbabilityPercent }];
        RiskMatrixDocument.WithCurrentFromHistory(risk);
        return risk;
    }

    private static IReadOnlyList<CatalogItem> Catalog(List<CatalogItemDto>? items)
    {
        var list = (items ?? []).Select(i => new CatalogItem(i.Id == Guid.Empty ? Guid.NewGuid() : i.Id, (i.Name ?? "").Trim())).ToList();
        foreach (var item in list)
        {
            Validation.ValidateCatalogName(item.Name);
        }

        return list;
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
        public string? Company { get; set; }
        public DateOnly? Date { get; set; }
        public ProjectStatus Status { get; set; }
        public byte[]? CompanyLogo { get; set; }
        public Guid? RiskScaleId { get; set; }
        public Guid? ImpactScaleId { get; set; }
        public Guid? ProbabilityScaleId { get; set; }
        public List<HeatCellDto>? HeatMap { get; set; }
        public List<CompanyDto>? Companies { get; set; }
        public List<CatalogItemDto>? Consequences { get; set; }
        public List<CatalogItemDto>? MaterialityCriteria { get; set; }
    }

    private sealed class CompanyDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public byte[]? Logo { get; set; }
        public List<CatalogItemDto>? Units { get; set; }
    }

    private sealed class CatalogItemDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class HeatCellDto
    {
        public Guid ImpactLevelId { get; set; }
        public Guid ProbabilityLevelId { get; set; }
        public Guid RiskLevelId { get; set; }
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
        public string? TextColor { get; set; }
        public int Order { get; set; }
        public decimal? RangeFrom { get; set; }
        public decimal? RangeTo { get; set; }
    }

    private sealed class RiskDto
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? ShortDescription { get; set; }
        public string? LongDescription { get; set; }
        public Guid ImpactLevelId { get; set; }
        public Guid ProbabilityLevelId { get; set; }
        public string? ImpactJustification { get; set; }
        public string? ProbabilityJustification { get; set; }
        public DateTime? CreatedUtc { get; set; }
        public DateTime? ModifiedUtc { get; set; }
        public decimal? ImpactAmount { get; set; }
        public decimal? ProbabilityPercent { get; set; }
        public Guid? CompanyId { get; set; }
        public Guid? UnitId { get; set; }
        public List<Guid>? ConsequenceIds { get; set; }
        public Guid? MaterialityId { get; set; }
        public List<AssessmentDto>? Assessments { get; set; }
    }

    private sealed class AssessmentDto
    {
        public Guid Id { get; set; }
        public DateOnly Date { get; set; }
        public Guid ImpactLevelId { get; set; }
        public Guid ProbabilityLevelId { get; set; }
        public string? Note { get; set; }
        public DateTime? RecordedUtc { get; set; }
        public decimal? ImpactAmount { get; set; }
        public decimal? ProbabilityPercent { get; set; }
    }
}
