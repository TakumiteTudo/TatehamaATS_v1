namespace TatehamaATS_v1
{
    public class OperationNotificationData
    {
        public string DisplayName { get; init; }
        public OperationNotificationType Type { get; set; }
        public string Content { get; set; }
        public DateTime OperatedAt { get; set; }
        public override string ToString()
        {
            return $"{DisplayName}:{Type}:{Content}:{OperatedAt}";
        }
    }

    public enum OperationNotificationType
    {
        None,
        Yokushi,
        Tsuuchi,
        TsuuchiKaijo,
        Kaijo,
        Shuppatsu,
        ShuppatsuJikoku,
        Torikeshi,
        Other,
        Class,
        Tenmatsusho
    }
}
