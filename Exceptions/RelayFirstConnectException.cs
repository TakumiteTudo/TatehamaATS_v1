namespace TatehamaATS_v1.Exceptions
{
    /// <summary>
    /// CD:継電部接続異常
    /// </summary>
    internal class RelayFirstConnectException : ATSCommonException
    {
        /// <summary>
        /// CD:継電部接続異常
        /// </summary>
        public RelayFirstConnectException(int place) : base(place)
        {
        }
        /// <summary>
        /// CD:継電部接続異常
        /// </summary>
        public RelayFirstConnectException(int place, string message)
            : base(place, message)
        {
        }
        /// <summary>
        /// CD:継電部接続異常
        /// </summary>
        public RelayFirstConnectException(int place, string message, Exception inner)
            : base(place, message, inner)
        {
        }
        public override string ToCode()
        {
            return Place.ToString() + "CC";
        }
        public override ResetConditions ResetCondition()
        {
            return ResetConditions.StopDetection_RelayReset;
        }
        public override OutputBrake ToBrake()
        {
            return OutputBrake.EB;
        }
    }
}
