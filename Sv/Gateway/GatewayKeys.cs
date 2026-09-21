using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.X509;

namespace Sv.Gateway;

public sealed class GatewayKeys : IDisposable
{
    private readonly RsaKeyParameters privateKey;
    private readonly object sync = new();

    public string PublicKeyPem { get; }
    public string Fingerprint { get; }
    public int KeySizeBytes => (privateKey.Modulus.BitLength + 7) / 8;

    public GatewayKeys(IHostEnvironment environment, IConfiguration configuration)
    {
        string configuredPath = configuration.GetValue<string>("Server:GatewayPrivateKeyPath")
            ?? "ServerData/gateway_private.pem";
        string privatePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        if (!File.Exists(privatePath))
            throw new FileNotFoundException($"找不到本地 Gateway RSA 私钥文件: {privatePath}");

        string pemText = File.ReadAllText(privatePath);
        using var reader = new StringReader(pemText);
        var pemReader = new PemReader(reader);
        object pemObj = pemReader.ReadObject()
            ?? throw new InvalidOperationException("无法解析 Gateway RSA 私钥 PEM 内容");

        privateKey = pemObj switch
        {
            AsymmetricCipherKeyPair pair => (RsaKeyParameters)pair.Private,
            RsaKeyParameters rsaKey when rsaKey.IsPrivate => rsaKey,
            _ => throw new InvalidOperationException($"不支持的私钥对象类型: {pemObj.GetType().FullName}")
        };

        if (privateKey.Modulus.BitLength < 1024)
            throw new InvalidOperationException("本地 Gateway RSA 密钥至少需要 1024 位");

        RsaKeyParameters pubParams = privateKey is RsaPrivateCrtKeyParameters crt
            ? new RsaKeyParameters(false, crt.Modulus, crt.PublicExponent)
            : new RsaKeyParameters(false, privateKey.Modulus, privateKey.Exponent);

        using var pubWriter = new StringWriter();
        var pemWriter = new PemWriter(pubWriter);
        pemWriter.WriteObject(pubParams);
        pemWriter.Writer.Flush();
        PublicKeyPem = pubWriter.ToString();

        var spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pubParams);
        Fingerprint = Convert.ToHexString(SHA256.HashData(spki.GetDerEncoded()));
    }

    public byte[] Decrypt(byte[] ciphertext)
    {
        lock (sync)
        {
            if (ciphertext.Length != KeySizeBytes)
                throw new InvalidDataException($"RSA 会话密钥密文长度错误，期望 {KeySizeBytes} 字节，实际 {ciphertext.Length} 字节");

            var engine = new OaepEncoding(new RsaEngine(), new Sha1Digest());
            engine.Init(false, privateKey);
            return engine.ProcessBlock(ciphertext, 0, ciphertext.Length);
        }
    }

    public void Dispose() { }
}
