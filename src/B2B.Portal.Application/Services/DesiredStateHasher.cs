using System.Security.Cryptography;
using System.Text;

namespace B2B.Portal.Application.Services;

/// <summary>
/// Erzeugt einen stabilen Hash über den fachlich gewünschten Zustand eines Jobs.
/// Wird für Idempotenzprüfungen genutzt: derselbe Grant-Job darf keinen doppelten
/// technischen Zustand erzeugen (MVP-Dokument Abschnitt 8, "Idempotenztest für
/// GrantWorkloadRole").
/// </summary>
public static class DesiredStateHasher
{
    public static string Hash(params string[] parts)
    {
        var input = string.Join('|', parts);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
