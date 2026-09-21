using System.Security.Cryptography;
using System.Text;

namespace Sv.Gateway;

public sealed class GatewayKeys : IDisposable
{
    private readonly RSA rsa;
    private readonly object sync = new();
    public string PublicKeyPem { get; }
    public string Fingerprint { get; }

    public GatewayKeys(IHostEnvironment environment)
    {
        string directory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(directory);
        string privatePath = Path.Combine(directory, "gateway-private.pem");
        rsa = RSA.Create(2048);
        if (File.Exists(privatePath))
        {
            rsa.ImportFromPem(File.ReadAllText(privatePath));
            if (rsa.KeySize < 2048)
                throw new InvalidOperationException("本地 Gateway RSA 密钥至少需要 2048 位");
        }
        else
        {
            using FileStream file = new(privatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] pem = Encoding.ASCII.GetBytes(rsa.ExportPkcs8PrivateKeyPem());
            try
            {
                file.Write(pem);
                file.Flush(true);
            }
            finally { CryptographicOperations.ZeroMemory(pem); }
        }
        // 公钥供 Redirector 读取；私钥不经过 HTTP 或 Frida 下发。
        PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        File.WriteAllText(Path.Combine(directory, "gateway-public.pem"), PublicKeyPem, Encoding.ASCII);
        Fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
    }

    public byte[] Decrypt(byte[] ciphertext)
    {
        lock (sync)
        {
            if (ciphertext.Length != rsa.KeySize / 8)
                throw new InvalidDataException("RSA 会话密钥长度错误");
            return rsa.Decrypt(ciphertext, RSAEncryptionPadding.OaepSHA1);
        }
    }

    public void Dispose() => rsa.Dispose();
}
