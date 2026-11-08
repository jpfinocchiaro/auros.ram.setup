namespace Auros.Ram.App.Tests;

/// <summary>
/// Textos de la matriz de ejemplo de las capturas, en el idioma de AUROS_LANGUAGE (español si no se indica). Así las
/// capturas de la landing muestran datos en el mismo idioma que la interfaz.
/// </summary>
internal sealed record SampleTexts(
    string MainFile,
    string OtherFile,
    string Project,
    string Company,
    string[] Risks,
    string Summary,
    string LongDescription,
    string Justification,
    string InitialNote,
    string[] Notes,
    string[] Actions,
    string[] Owners)
{
    public static SampleTexts Current =>
        (Environment.GetEnvironmentVariable("AUROS_LANGUAGE") ?? "es").ToLowerInvariant() switch
        {
            "en" => English,
            "pt" => Portuguese,
            "fr" => French,
            _ => Spanish,
        };

    private static readonly SampleTexts Spanish = new(
        "Relevamiento ACME.rmx",
        "Auditoría 2025.rmx",
        "Relevamiento de seguridad 2026",
        "ACME S.A.",
        [
            "Cuentas de administrador compartidas",
            "Sin copias de seguridad fuera del sitio",
            "Contraseñas sin vencimiento",
            "Parches de servidores atrasados",
            "Falta de plan de continuidad",
            "Inventario de activos incompleto",
            "Registros de auditoría desactivados",
            "Accesos de ex empleados vigentes",
        ],
        "Resumen ejecutivo: {0}.",
        """
        {g}Hallazgo{/g}
        Se detectaron **cuentas activas** de personas que ya no trabajan en la empresa.
        *Análisis:* el acceso permite __modificar datos de clientes__.
        Recomendación:
        - Revisar las bajas cada mes
        - Automatizar el bloqueo
        1. Inventario
        2. Depuración
        """,
        "Expone información sensible de clientes.",
        "Relevamiento inicial",
        [
            "Se detectaron más cuentas compartidas",
            "Se aplicó un control de acceso",
            "Cuentas nominales en todos los servidores",
            "Se automatizó la instalación de parches",
            "Se amplió el alcance del sistema",
        ],
        [
            "Cuentas nominales para cada administrador",
            "Revisión trimestral de accesos privilegiados",
            "Bóveda de contraseñas",
            "Copia diaria en un sitio externo",
        ],
        ["Infraestructura", "Seguridad informática"]);

    private static readonly SampleTexts English = new(
        "ACME review.rmx",
        "Audit 2025.rmx",
        "Security review 2026",
        "ACME Inc.",
        [
            "Shared administrator accounts",
            "No off-site backups",
            "Passwords that never expire",
            "Outdated server patches",
            "No business continuity plan",
            "Incomplete asset inventory",
            "Audit logs disabled",
            "Former employees still have access",
        ],
        "Executive summary: {0}.",
        """
        {g}Finding{/g}
        **Active accounts** were found for people who no longer work at the company.
        *Analysis:* this access allows __changing customer data__.
        Recommendation:
        - Review departures every month
        - Automate account locking
        1. Inventory
        2. Cleanup
        """,
        "Exposes sensitive customer information.",
        "Initial review",
        [
            "More shared accounts were found",
            "An access control was put in place",
            "Named accounts on every server",
            "Patch installation was automated",
            "The system's scope was expanded",
        ],
        [
            "Named accounts for each administrator",
            "Quarterly review of privileged access",
            "Password vault",
            "Daily backup to an off-site location",
        ],
        ["Infrastructure", "IT security"]);

    private static readonly SampleTexts Portuguese = new(
        "Levantamento ACME.rmx",
        "Auditoria 2025.rmx",
        "Levantamento de segurança 2026",
        "ACME S.A.",
        [
            "Contas de administrador compartilhadas",
            "Sem cópias de segurança externas",
            "Senhas sem expiração",
            "Atualizações de servidores atrasadas",
            "Falta de plano de continuidade",
            "Inventário de ativos incompleto",
            "Registros de auditoria desativados",
            "Acessos de ex-funcionários ativos",
        ],
        "Resumo executivo: {0}.",
        """
        {g}Constatação{/g}
        Foram detectadas **contas ativas** de pessoas que não trabalham mais na empresa.
        *Análise:* o acesso permite __alterar dados de clientes__.
        Recomendação:
        - Revisar os desligamentos todo mês
        - Automatizar o bloqueio
        1. Inventário
        2. Depuração
        """,
        "Expõe informações sensíveis de clientes.",
        "Levantamento inicial",
        [
            "Foram detectadas mais contas compartilhadas",
            "Foi aplicado um controle de acesso",
            "Contas nominais em todos os servidores",
            "A instalação de atualizações foi automatizada",
            "O escopo do sistema foi ampliado",
        ],
        [
            "Contas nominais para cada administrador",
            "Revisão trimestral de acessos privilegiados",
            "Cofre de senhas",
            "Cópia diária em um local externo",
        ],
        ["Infraestrutura", "Segurança da informação"]);

    private static readonly SampleTexts French = new(
        "Diagnostic ACME.rmx",
        "Audit 2025.rmx",
        "Diagnostic de sécurité 2026",
        "ACME SA",
        [
            "Comptes administrateur partagés",
            "Aucune sauvegarde hors site",
            "Mots de passe sans expiration",
            "Correctifs serveurs en retard",
            "Absence de plan de continuité",
            "Inventaire des actifs incomplet",
            "Journaux d'audit désactivés",
            "Accès d'anciens employés actifs",
        ],
        "Synthèse : {0}.",
        """
        {g}Constat{/g}
        Des **comptes actifs** de personnes qui ne travaillent plus dans l'entreprise ont été détectés.
        *Analyse :* cet accès permet de __modifier les données des clients__.
        Recommandation :
        - Vérifier les départs chaque mois
        - Automatiser le blocage
        1. Inventaire
        2. Nettoyage
        """.Replace(" :", " :", StringComparison.Ordinal),
        "Expose des informations sensibles sur les clients.",
        "Diagnostic initial",
        [
            "D'autres comptes partagés ont été détectés",
            "Un contrôle d'accès a été mis en place",
            "Comptes nominatifs sur tous les serveurs",
            "L'installation des correctifs a été automatisée",
            "Le périmètre du système a été élargi",
        ],
        [
            "Comptes nominatifs pour chaque administrateur",
            "Revue trimestrielle des accès privilégiés",
            "Coffre-fort de mots de passe",
            "Sauvegarde quotidienne sur un site externe",
        ],
        ["Infrastructure", "Sécurité informatique"]);
}
