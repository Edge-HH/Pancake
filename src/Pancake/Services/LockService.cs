using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Pancake.Services;

/// <summary>
/// 锁定功能的纯逻辑：密码哈希校验、2FA（TOTP）验证码与锁定生效条件。
/// 不依赖界面，便于在 SettingsLogic 中直接测试。
/// </summary>
public static class LockService
{
    public const int PasswordIterations = 100_000;
    public const int TotpDigits = 6;
    public const int TotpStepSeconds = 30;

    /// <summary>锁定只在总开关开启且密码、2FA 至少配置一个时生效。</summary>
    public static bool IsEnforced(LockSettings settings) =>
        settings.Enabled && (HasPassword(settings) || HasTotp(settings));

    public static bool HasPassword(LockSettings settings) =>
        !string.IsNullOrEmpty(settings.PasswordHash) && !string.IsNullOrEmpty(settings.PasswordSalt);

    public static bool HasTotp(LockSettings settings) => !string.IsNullOrEmpty(settings.TotpSecret);

    public static void SetPassword(LockSettings settings, string password)
    {
        settings.PasswordSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        settings.PasswordHash = HashPassword(password, settings.PasswordSalt);
    }

    public static void ClearPassword(LockSettings settings)
    {
        settings.PasswordSalt = "";
        settings.PasswordHash = "";
    }

    public static string HashPassword(string password, string saltBase64) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(saltBase64),
            PasswordIterations, HashAlgorithmName.SHA256, 32));

    public static bool VerifyPassword(LockSettings settings, string password) =>
        HasPassword(settings) && VerifyPassword(password, settings.PasswordSalt, settings.PasswordHash);

    public static bool VerifyPassword(string password, string saltBase64, string hashBase64)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(hashBase64))
            return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(HashPassword(password, saltBase64)), Convert.FromBase64String(hashBase64));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>生成验证器应用使用的 Base32 密钥（160 位，与常见验证器兼容）。</summary>
    public static string CreateTotpSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    /// <summary>验证器应用手动添加或扫码使用的 otpauth 链接。</summary>
    public static string TotpProvisioningUri(string secret, string account = "Pancake") =>
        $"otpauth://totp/{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(account)}&digits={TotpDigits}&period={TotpStepSeconds}";

    /// <summary>校验 2FA 验证码；window 为允许的前后时间步数，默认容忍一步（30 秒）时钟偏差。</summary>
    public static bool VerifyTotp(string secret, string code, DateTimeOffset now, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        string digits = new([.. code.Where(char.IsDigit)]);
        if (digits.Length != TotpDigits) return false;
        byte[] key;
        try { key = Base32Decode(secret); }
        catch (FormatException) { return false; }
        long step = now.ToUnixTimeSeconds() / TotpStepSeconds;
        for (long offset = -window; offset <= window; offset++)
            if (ComputeTotp(key, Math.Max(0, step + offset)) == digits) return true;
        return false;
    }

    /// <summary>RFC 6238 的 TOTP：HMAC-SHA1、6 位数字、30 秒一个时间步。</summary>
    public static string ComputeTotp(byte[] key, long step)
    {
        byte[] counter = BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(step));
        byte[] hash = HMACSHA1.HashData(key, counter);
        int offset = hash[^1] & 0x0F;
        int code = (hash[offset] & 0x7F) << 24 | hash[offset + 1] << 16 | hash[offset + 2] << 8 | hash[offset + 3];
        return (code % 1_000_000).ToString("D6");
    }

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Base32Encode(byte[] data)
    {
        StringBuilder result = new();
        int buffer = 0, bits = 0;
        foreach (byte b in data)
        {
            buffer = buffer << 8 | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Append(Base32Alphabet[buffer >> bits & 0x1F]);
            }
        }
        if (bits > 0) result.Append(Base32Alphabet[buffer << (5 - bits) & 0x1F]);
        return result.ToString();
    }

    public static byte[] Base32Decode(string text)
    {
        List<byte> result = [];
        int buffer = 0, bits = 0;
        foreach (char c in text.Trim().TrimEnd('=').ToUpperInvariant())
        {
            if (c == ' ' || c == '-') continue;
            int value = Base32Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException("非法的 Base32 字符");
            buffer = buffer << 5 | value;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            result.Add((byte)(buffer >> bits & 0xFF));
        }
        return [.. result];
    }
}
