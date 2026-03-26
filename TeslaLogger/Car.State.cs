namespace TeslaLogger
{
    internal partial class Car
    {

        internal enum TeslaState
        {
            Start,
            Drive,
            Charge,
            Sleep,
            Online,
            GoSleep,
            Inactive
        }

        private TeslaState _currentState = TeslaState.Start;
    }
}


