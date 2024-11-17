﻿namespace TatehamaATS_v1.Exceptions
{
    /// <summary>
    /// EE:通信部カウンタ異常
    /// </summary>
    internal class SocketCountaException : ATSCommonException
    {
        /// <summary>
        /// EE:通信部カウンタ異常
        /// </summary>
        public SocketCountaException(int place) : base(place)
        {
        }
        /// <summary>
        /// EE:通信部カウンタ異常
        /// </summary>
        public SocketCountaException(int place, string message)
            : base(place, message)
        {
        }
        /// <summary>
        /// EE:通信部カウンタ異常
        /// </summary>
        public SocketCountaException(int place, string message, Exception inner)
            : base(place, message, inner)
        {
        }
        public override string ToCode()
        {
            return Place.ToString() + "EE";
        }
        public override ResetConditions ResetCondition()
        {
            return ResetConditions.RetsubanReset;
        }
        public override OutputBrake ToBrake()
        {
            return OutputBrake.EB;
        }
    }
}