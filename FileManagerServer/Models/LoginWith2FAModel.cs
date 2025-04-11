using System.ComponentModel.DataAnnotations;

namespace FileManagerServer.Models
{
    public class LoginWith2FAModel
    {
        [Required(ErrorMessage = "Username is required")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Two-factor code is required")]
        public string TwoFACode { get; set; }
    }
}