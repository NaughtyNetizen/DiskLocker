using BCryptNet = BCrypt.Net.BCrypt;

namespace DiskLocker.Services;

/// <summary>
/// 密码哈希与验证服务（基于 BCrypt 算法）
/// </summary>
public class PasswordService
{
    private const int WorkFactor = 12;

    /// <summary>对密码进行 BCrypt 哈希</summary>
    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("密码不能为空", nameof(password));
        return BCryptNet.HashPassword(password, WorkFactor);
    }

    /// <summary>验证密码是否匹配哈希值</summary>
    public bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;
        try
        {
            return BCryptNet.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>验证密码强度（最小长度 6 位）</summary>
    public static bool ValidatePasswordStrength(string password, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            errorMessage = "密码不能为空";
            return false;
        }
        if (password.Length < 6)
        {
            errorMessage = "密码长度至少为 6 位";
            return false;
        }
        errorMessage = string.Empty;
        return true;
    }
}
