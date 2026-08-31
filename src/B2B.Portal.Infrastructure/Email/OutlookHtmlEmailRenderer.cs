namespace B2B.Portal.Infrastructure.Email;

/// <summary>
/// Wrappt vom Admin editierten Inhalt (einfaches Absatz-/Link-HTML, siehe
/// ReminderPolicyPage-Editor) in ein Outlook-Desktop-kompatibles Grundgeruest.
///
/// Outlook Desktop (Win32) rendert HTML-Mails NICHT ueber einen Browser-Renderer, sondern
/// ueber die Word-Rendering-Engine — die kennt kein Flexbox/Grid, kein &lt;style&gt;-Block-CSS
/// zuverlaessig, und braucht Tabellen fuer jedes Layout. Dieses Geruest kapselt genau die
/// Standard-Workarounds (siehe z.B. https://www.caniemail.com fuer Outlook-Support-Matrix):
/// - Aeusseres &lt;table&gt; statt &lt;div&gt; fuer das Layout, feste Breite (600px, gaengiger
///   Mail-Standard).
/// - Alle Styles inline (style="..."), kein &lt;style&gt;-Block — Outlook filtert externe/interne
///   Stylesheets in vielen Versionen weg.
/// - MSO-Conditional-Comments (&lt;!--[if mso]&gt;) fuer eine feste Pixel-Breite, die andere
///   Clients (Gmail, Apple Mail) ignorieren, Outlook aber braucht, um die Tabelle nicht auf
///   Bildschirmbreite zu strecken.
/// - Explizite Font-Family mit Web-sicherem Fallback-Stack (kein Google-Font-Import, den
///   Outlook nicht laedt).
///
/// Der Admin editiert NUR den inneren Inhalt (siehe ReminderStage.TemplateBody) — dieser
/// Renderer ist die einzige Stelle, die das Outlook-Geruest kennt, damit Versand
/// (InvitationReminderHandler) und Vorschau (GET /api/reminder-policy/preview) exakt
/// dasselbe Markup erzeugen.
/// </summary>
public static class OutlookHtmlEmailRenderer
{
    public static string Render(string subject, string innerBodyHtml)
    {
        // subject wird hier nur als <title> im HTML-Head gespiegelt (rein informativ fuer
        // Vorschau/Debugging) — der tatsaechliche Mail-Betreff kommt aus dem SMTP/Graph-Header,
        // nicht aus dem HTML-Body selbst.
        var encodedSubject = System.Net.WebUtility.HtmlEncode(subject);

        return $$"""
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <meta http-equiv="X-UA-Compatible" content="IE=edge" />
            <title>{{encodedSubject}}</title>
            <!--[if mso]>
            <noscript>
            <xml>
            <o:OfficeDocumentSettings>
            <o:PixelsPerInch>96</o:PixelsPerInch>
            </o:OfficeDocumentSettings>
            </xml>
            </noscript>
            <![endif]-->
            </head>
            <body style="margin:0; padding:0; background-color:#f4f4f5; font-family:Segoe UI, Arial, Helvetica, sans-serif;">
            <!--[if mso]>
            <table role="presentation" width="600" align="center" cellpadding="0" cellspacing="0" border="0"><tr><td>
            <![endif]-->
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px; margin:0 auto; background-color:#ffffff;">
            <tr>
            <td style="padding:24px 32px; font-family:Segoe UI, Arial, Helvetica, sans-serif; font-size:14px; line-height:1.5; color:#1a1a1a;">
            {{innerBodyHtml}}
            </td>
            </tr>
            </table>
            <!--[if mso]>
            </td></tr></table>
            <![endif]-->
            </body>
            </html>
            """;
    }
}
