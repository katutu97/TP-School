namespace TP_School.ViewModels
{
    // Модель для обработки данных, отправленных из формы "Написать сообщение"
    public class SendMessageViewModel
    {
        // ID получателя (соответствует полю формы RecipientId)
        public int RecipientId { get; set; }

        // Текст сообщения (соответствует полю формы Body)
        public string Body { get; set; }

        // Дополнительные поля, если нужны: Subject, ConversationId и т.д.
    }
}