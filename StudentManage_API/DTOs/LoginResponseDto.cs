namespace StudentManage_API.DTOs
{
    public class LoginResponseDto
    {
        public string Token { get; set; }
        public DateTime Expires { get; set; }
        public UserInfoDto User { get; set; }
    }
}
