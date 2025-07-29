using StudentManage_API.DTOs;

namespace StudentManage_API.Interfaces
{
    public interface IAuthService
    {
        Task<ApiResponseDto<LoginResponseDto>> LoginAsync(LoginRequestDto request);
        Task<ApiResponseDto<bool>> ChangePasswordAsync(int userId, ChangePasswordDto request);
        string GenerateJwtToken(UserInfoDto user);
        Task<UserInfoDto> GetUserInfoAsync(int userId);
        Task<bool> ValidateUserCredentialsAsync(string username, string password);
    }
}