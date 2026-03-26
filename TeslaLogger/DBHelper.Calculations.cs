using System;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625

#nullable enable

namespace TeslaLogger
{
    [SuppressMessage("Globalization", "CA1303:Literale nicht als lokalisierte Parameter übergeben", Justification = "<Pending>")]
    [SuppressMessage("Design", "CA1031:Keine allgemeinen Ausnahmetypen abfangen", Justification = "<Pending>")]
    public partial class DBHelper
    {
        /// <summary>
        /// Calculates the actual charging current, validating against requested limits.
        /// </summary>
        /// <param name="actualCurrent">The measured charging current in Amps</param>
        /// <param name="requestedCurrent">The requested/limited charging current in Amps</param>
        /// <returns>The effective charging current to use, or 0 if invalid</returns>
        public static int CalculateCurrent(int actualCurrent, int requestedCurrent)
        {
            if (actualCurrent < 1 || requestedCurrent < 1)
                return 0;

            if (actualCurrent > requestedCurrent && requestedCurrent < 6)
                return requestedCurrent;

            return actualCurrent;
        }

        /// <summary>
        /// Calculates the number of electrical phases from power, voltage, and current.
        /// </summary>
        /// <param name="power">Power in kW</param>
        /// <param name="voltage">Voltage in V</param>
        /// <param name="current">Current in A</param>
        /// <returns>Number of phases (1-3), or 0 if invalid inputs</returns>
        /// <remarks>
        /// Formula: phases = (power * 1000 + 500) / (voltage * current)
        /// Constrained to valid range: 1-3 phases
        /// </remarks>
        public static int CalculatePhases(int power, int voltage, int current)
        {
            if (power <= 0 || voltage <= 0 || current <= 0 )
                return 0;

            int phases = Convert.ToInt32(Math.Truncate(Math.Truncate((power * 1000.0 + 500) / voltage / current))+0.3);
            
            if (phases > 3)
                return 3;

            if (phases < 1)
                return 1;
            
            return phases;
        }
        
        /// <summary>
        /// Calculates electrical power from voltage, phases, and current.
        /// Formula: Power = Voltage × Phases × Current (in kW)
        /// </summary>
        /// <param name="voltage">Voltage in V</param>
        /// <param name="phases">Number of electrical phases (1-3)</param>
        /// <param name="current">Current in A</param>
        /// <returns>Power in kW, or 0 if invalid inputs</returns>
        public static int CalculatePower(int voltage, int phases, int current)
        {
            if (voltage < 0 || phases < 1 || current < 1)
                return 0;
                       
            return phases * voltage * current;
        }
    }
}
