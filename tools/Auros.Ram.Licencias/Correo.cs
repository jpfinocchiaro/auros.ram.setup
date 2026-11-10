using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace Auros.Ram.Licencias;

/// <summary>
/// Envía la licencia por email al cliente, con la cuenta de correo configurada en correo.json (en la carpeta de las
/// licencias, nunca en el repositorio).
/// </summary>
internal static class Correo
{
    public const string FileName = "correo.json";

    public static readonly string[] Languages = ["es", "en", "pt", "fr"];

    private const string Template = """
        {
          "servidor": "smtp.gmail.com",
          "puerto": 587,
          "usuario": "tu-cuenta@gmail.com",
          "clave": "",
          "remitente": "auros.ram <tu-cuenta@gmail.com>",
          "copia": "tu-cuenta@gmail.com"
        }
        """;

    private sealed record Settings(string Servidor, int Puerto, string Usuario, string Clave, string Remitente, string Copia);

    /// <summary>Envía la licencia. Devuelve null si salió bien, o el motivo por el que no se pudo enviar.</summary>
    public static string? Send(string folder, string to, string customer, string machine, string license, string language)
    {
        var file = Path.Combine(folder, FileName);
        if (!File.Exists(file))
        {
            File.WriteAllText(file, Template, new UTF8Encoding(false));
            return $"falta configurar la cuenta de correo. Completa {file} (ver README) y usa «reenviar».";
        }

        Settings settings;
        try
        {
            settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip })
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return $"{file} no tiene el formato esperado.";
        }

        var password = settings.Clave is { Length: > 0 } ? settings.Clave : Environment.GetEnvironmentVariable("AUROS_SMTP_CLAVE") ?? "";
        if (settings.Usuario.Contains("tu-cuenta", StringComparison.Ordinal) || password.Length == 0)
        {
            return $"falta completar el usuario y la clave en {file} (o la variable AUROS_SMTP_CLAVE).";
        }

        var (subject, body) = Message(language, customer, machine, license);
        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(settings.Remitente is { Length: > 0 } ? settings.Remitente : settings.Usuario),
                Subject = subject,
                Body = body,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
            };
            message.To.Add(to);
            if (settings.Copia is { Length: > 0 })
            {
                message.Bcc.Add(settings.Copia);
            }

            using var client = new SmtpClient(settings.Servidor, settings.Puerto)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(settings.Usuario, password),
                Timeout = 30000,
            };
            client.Send(message);
            return null;
        }
        catch (Exception ex) when (ex is SmtpException or FormatException or InvalidOperationException)
        {
            return ex.InnerException is { } inner ? $"{ex.Message} ({inner.Message})" : ex.Message;
        }
    }

    public static (string Subject, string Body) Message(string language, string customer, string machine, string license)
    {
        var date = DateTime.Today.ToString("d", CultureInfo.GetCultureInfo(language));
        return language switch
        {
            "en" => ("Your auros.ram license",
                $"""
                Hello,

                Thank you for buying auros.ram. This is your license for "{customer}":

                {license}

                To activate it:
                1. Open auros.ram on the computer with code {machine}.
                2. Go to Settings → License.
                3. Paste the key in "License key" and click Activate.

                The key works only on that computer. If you change computers or reinstall the system, write to us and we will send you a new one.

                Best regards,
                Auros · auros.ram
                Issued on {date}
                """),
            "pt" => ("Sua licença do auros.ram",
                $"""
                Olá,

                Obrigado por comprar o auros.ram. Esta é a sua licença para "{customer}":

                {license}

                Para ativá-la:
                1. Abra o auros.ram no computador com o código {machine}.
                2. Vá em Configurações → Licença.
                3. Cole a chave em "Chave de licença" e clique em Ativar.

                A chave funciona apenas nesse computador. Se trocar de computador ou reinstalar o sistema, escreva para nós e enviaremos uma nova.

                Atenciosamente,
                Auros · auros.ram
                Emitida em {date}
                """),
            "fr" => ("Votre licence auros.ram",
                $"""
                Bonjour,

                Merci d'avoir acheté auros.ram. Voici votre licence pour « {customer} » :

                {license}

                Pour l'activer :
                1. Ouvrez auros.ram sur l'ordinateur dont le code est {machine}.
                2. Allez dans Paramètres → Licence.
                3. Collez la clé dans « Clé de licence » et cliquez sur Activer.

                La clé ne fonctionne que sur cet ordinateur. Si vous changez d'ordinateur ou réinstallez le système, écrivez-nous et nous vous en enverrons une nouvelle.

                Cordialement,
                Auros · auros.ram
                Émise le {date}
                """),
            _ => ("Tu licencia de auros.ram",
                $"""
                Hola:

                Gracias por comprar auros.ram. Esta es tu licencia para «{customer}»:

                {license}

                Para activarla:
                1. Abre auros.ram en el equipo con el código {machine}.
                2. Ve a Configuración → Licencia.
                3. Pega la clave en «Clave de licencia» y haz clic en Activar.

                La clave funciona solo en ese equipo. Si cambias de equipo o reinstalas el sistema, escríbenos y te enviamos una nueva.

                Saludos,
                Auros · auros.ram
                Emitida el {date}
                """),
        };
    }
}
