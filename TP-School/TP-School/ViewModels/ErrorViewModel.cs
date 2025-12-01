using System.Diagnostics;
namespace TP_School.ViewModels
{
    public class ErrorViewModel
    {
        // Идентификатор запроса для отслеживания ошибки
        public string? RequestId { get; set; }

        // Определяет, нужно ли показывать RequestId (полезно только в режиме разработки)
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
