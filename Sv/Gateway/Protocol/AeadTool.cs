using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.X509;
using Mobile.Server;

namespace Sv.Gateway.Protocol;

/// <summary>
/// 加密算法大全
/// </summary>
public static class AeadTool
{
    private static readonly Lock SyncRoot = new();
    private static RsaKeyParameters? _privateKey;

    public static string PublicKeyPem { get; private set; } = string.Empty;
    public static string Fingerprint { get; private set; } = string.Empty;
    public static int KeySizeBytes => _privateKey is not null ? (_privateKey.Modulus.BitLength + 7) / 8 : 128;

    /// <summary>
    /// 初始化 Gateway RSA 密钥对。
    /// </summary>
    public static void InitializeKeys(string privateKeyPath)
    {
        if (!File.Exists(privateKeyPath))
        {
            throw new FileNotFoundException($"找不到 Gateway RSA 私钥文件: {privateKeyPath}");
        }

        string pemText = File.ReadAllText(privateKeyPath);
        using var reader = new StringReader(pemText);
        var pemReader = new PemReader(reader);
        object pemObj = pemReader.ReadObject() ?? throw new InvalidOperationException("无法解析 Gateway RSA 私钥 PEM 内容");

        RsaKeyParameters privKey = pemObj switch
        {
            AsymmetricCipherKeyPair pair => (RsaKeyParameters)pair.Private,
            RsaKeyParameters rsaKey when rsaKey.IsPrivate => rsaKey,
            _ => throw new InvalidOperationException($"不支持的私钥对象类型: {pemObj.GetType().FullName}")
        };

        if (privKey.Modulus.BitLength < 1024)
        {
            throw new InvalidOperationException("Gateway RSA 密钥至少需要 1024 位");
        }

        RsaKeyParameters pubParams = privKey is RsaPrivateCrtKeyParameters crt ? new RsaKeyParameters(false, crt.Modulus, crt.PublicExponent) : new RsaKeyParameters(false, privKey.Modulus, privKey.Exponent);

        using var pubWriter = new StringWriter();
        var pemWriter = new PemWriter(pubWriter);
        pemWriter.WriteObject(pubParams);
        pemWriter.Writer.Flush();
        PublicKeyPem = pubWriter.ToString();

        var spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pubParams);
        Fingerprint = Convert.ToHexString(SHA256.HashData(spki.GetDerEncoded()));

        lock (SyncRoot)
        {
            _privateKey = privKey;
        }
    }

    /// <summary>
    /// 使用 RSA-OAEP (SHA-1) 解密客户端上报的会话密钥。
    /// </summary>
    public static byte[] DecryptRsaOaep(byte[] ciphertext)
    {
        lock (SyncRoot)
        {
            if (_privateKey is null)
            {
                throw new InvalidOperationException("AeadTool 尚未初始化 RSA 私钥，请先调用 AeadTool.InitializeKeys()");
            }

            if (ciphertext.Length != KeySizeBytes)
            {
                throw new InvalidDataException($"RSA 会话密钥密文长度错误，期望 {KeySizeBytes} 字节，实际 {ciphertext.Length} 字节");
            }

            var engine = new OaepEncoding(new RsaEngine(), new Sha1Digest());
            engine.Init(false, _privateKey);
            return engine.ProcessBlock(ciphertext, 0, ciphertext.Length);
        }
    }

    /// <summary>
    /// 创建并初始化 RC4 引擎。
    /// </summary>
    public static RC4Engine CreateRc4Engine(byte[] key, bool forEncryption)
    {
        var engine = new RC4Engine();
        engine.Init(forEncryption, new KeyParameter(key));
        return engine;
    }

    /// <summary>
    /// 计算实体 RPC 方法名的 MD5 散列（16 字节）。
    /// </summary>
    public static byte[] HashMethodName(string methodName)
    {
        return MD5.HashData(Encoding.UTF8.GetBytes(methodName));
    }

    /// <summary>
    /// 计算方法名的十六进制字符串（大写）。
    /// </summary>
    public static string HashMethodNameHex(string methodName)
    {
        return Convert.ToHexString(HashMethodName(methodName));
    }

    /// <summary>
    /// 生成实体方法的 Md5OrIndex 描述符。
    /// </summary>
    public static Md5OrIndex EncodeMethodName(string methodName)
    {
        return new Md5OrIndex
        {
            Md5 = ByteString.CopyFrom(HashMethodName(methodName))
        };
    }
}
