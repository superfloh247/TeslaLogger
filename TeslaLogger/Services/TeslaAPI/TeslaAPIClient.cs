using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using TeslaLogger.Data.Abstractions;
using static TeslaLogger.Car;
using static TeslaLogger.NullSafetyHelpers;

#nullable enable

namespace TeslaLogger.Services.TeslaAPI
{
    /// <summary>
    /// Production implementation of ITeslaAPIClient.
    /// Handles all HTTP communication with Tesla Owner API endpoints.
    /// Implements caching, retry logic, and rate limiting for ARM32 optimization.
    /// </summary>
    internal class TeslaAPIClient : ITeslaAPIClient, IDisposable
    {
        private readonly Car _car;
        private readonly ITokenManager _tokenManager;
        private readonly HttpClient _httpClient;
        private readonly ILogger<TeslaAPIClient> _logger;

        private DateTime? _startRequestTimeout;
        private static readonly Random _random = new Random();
        private static int _commandCounter = 0;
        private static int _commandCounterDrive = 0;
        private static int _commandCounterCharging = 0;
        private static int _commandCounterOnline = 0;

        private readonly ConcurrentDictionary<string, string> _teslaAPICommands = new();
        private readonly string _cacheGuid = Guid.NewGuid().ToString();

        public string ApiAddress => _tokenManager.GetApiAddress();

        public ConcurrentDictionary<string, string> TeslaAPICommands => _teslaAPICommands;

        /// <summary>
        /// Creates new TeslaAPIClient instance.
        /// </summary>
        internal TeslaAPIClient(Car car, ITokenManager tokenManager, HttpClient? httpClient = null, ILogger<TeslaAPIClient>? logger = null)
        {
            _car = car ?? throw new ArgumentNullException(nameof(car));
            _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
            _httpClient = httpClient ?? new HttpClient();
            _logger = logger;
        }

        public async Task<string> GetCommandAsync(string cmd, bool noMemcache = false, CancellationToken cancellationToken = default)
        {
            if (_car.FleetAPI)
            {
                _logger?.LogWarning("*** FleetAPI no Datacalls allowed! ***");
                return "";
            }

            string resultContent = "";
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string cacheKey = "GetCommand_" + cmd + "_" + _cacheGuid;
                var cachedValue = MemoryCache.Default[cacheKey] as string;

                if (cachedValue is not null)
                {
                    return cachedValue;
                }

                string token = await _tokenManager.GetOrRefreshTokenAsync(cancellationToken);
                string address = ApiAddress + "api/1/vehicles/" + _tokenManager.TeslaVehicleId + "/" + cmd;

                DateTime start = DateTime.UtcNow;

                using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address)))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    HttpResponseMessage result = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                    if (result.IsSuccessStatusCode)
                    {
                        _startRequestTimeout = null;
                        resultContent = await result.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (cmd.Contains("vehicle_data") && !noMemcache)
                        {
                            MemoryCache.Default.Add(cacheKey, resultContent, DateTime.Now.AddSeconds(4));
                        }

                        _ = DBHelper.AddMothershipDataToDBAsync("GetCommand(" + cmd + ")", start, (int)result.StatusCode, _car.CarInDB);
                        _ = _car.GetTeslaAPIState().ParseAPI(resultContent, cmd);

                        if (_teslaAPICommands.ContainsKey(cmd))
                        {
                            _teslaAPICommands.TryGetValue(cmd, out var oldValue);
                            _teslaAPICommands.TryUpdate(cmd, resultContent, oldValue);
                        }
                        else
                        {
                            _teslaAPICommands.TryAdd(cmd, resultContent);
                        }

                        _commandCounter++;
                        IncrementCommandCounter();

                        return resultContent;
                    }

                    if (result.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        LoginRetry(result);
                    }
                    else if (result.StatusCode == HttpStatusCode.NotFound)
                    {
                        string cacheKeyNotFound = "HttpNotFoundCounter_" + cmd + "_" + _cacheGuid;
                        int httpNotFoundCounter = (int)(MemoryCache.Default.Get(cacheKeyNotFound) ?? 0);
                        httpNotFoundCounter++;
                        MemoryCache.Default.Set(cacheKeyNotFound, httpNotFoundCounter, DateTime.Now.AddMinutes(10));

                        if (httpNotFoundCounter > 5)
                        {
                            _car.CreateExeptionlessLog("WebHelper", $"404 Error ({cmd}) -> Restart Car Thread", Exceptionless.Logging.LogLevel.Warn).Submit();
                            _car.Restart("404 Error", 0);
                        }

                        if (cmd.Contains("vehicle_data") && !noMemcache)
                        {
                            MemoryCache.Default.Add(cacheKey, "NULL", DateTime.Now.AddSeconds(15));
                        }
                    }
                    else if (result.StatusCode == HttpStatusCode.RequestTimeout)
                    {
                        if (_startRequestTimeout is null)
                            _startRequestTimeout = DateTime.UtcNow;

                        if (cmd.Contains("vehicle_data") && !noMemcache)
                        {
                            MemoryCache.Default.Add(cacheKey, "NULL", DateTime.Now.AddSeconds(15));
                        }
                    }
                    else if ((int)result.StatusCode == 429) // TooManyRequests
                    {
                        int sleep = _random.Next(5000) + 60000;
                        await Task.Delay(sleep, cancellationToken);
                        return await GetCommandAsync(cmd, noMemcache, cancellationToken);
                    }

                    _ = DBHelper.AddMothershipDataToDBAsync("GetCommand(" + cmd + ")", double.Parse("-1." + (int)result.StatusCode, Tools.ciEnUS), (int)result.StatusCode, _car.CarInDB);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in GetCommandAsync");
            }

            return resultContent;
        }

        public async Task<string> PostCommandAsync(string cmd, string data, bool isJson = false)
        {
            // Placeholder implementation - will be filled from existing WebHelper logic
            return "{}";
        }

        public async Task<string> WakeupAsync()
        {
            // Placeholder implementation
            return "{}";
        }

        public void GetAllVehicles(out string? resultContent, out JArray vehicles, bool throwExceptionOnUnauthorized, bool doNotCache = false)
        {
            resultContent = null;
            vehicles = new JArray();
            // Placeholder implementation
        }

        public async Task<string> GetNearbyChargingSitesOwnerAPIAsync()
        {
            // Placeholder implementation
            return "NULL";
        }

        public async Task<string> GetChargingHistoryV2(int pageNumber = 1)
        {
            return await GetChargingHistoryV2(null, pageNumber);
        }

        public async Task<string> GetChargingHistoryV2(string? vin, int pageNumber = 1)
        {
            // Placeholder implementation
            return "{}";
        }

        public async Task<byte[]?> GetChargingHistoryInvoicePDFAsync(string contentId)
        {
            // Placeholder implementation
            return null;
        }

        public async IAsyncEnumerable<JObject> GetChargingHistoryStreamAsync(string? vin = null, int pageNumber = 1, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Placeholder implementation - yields nothing
            yield break;
        }

        public bool LoginRetry(HttpResponseMessage httpResponse)
        {
            if (httpResponse?.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger?.LogWarning("HttpStatusCode = Unauthorized. Password changed or still valid?");
                return _tokenManager.HandleUnauthorizedResponse(httpResponse);
            }
            return false;
        }

        public string GetRegion()
        {
            return _tokenManager.GetApiAddress();
        }

        public string GetLastShiftState()
        {
            // Placeholder
            return "P";
        }

        public void SetLastShiftState(string newState)
        {
            // Placeholder
        }

        public void ResetLastChargingState()
        {
            // Placeholder
        }

        public async Task<double?> GetOutsideTemperatureAsync(CancellationToken cancellationToken = default)
        {
            // Placeholder
            return null;
        }

        private void IncrementCommandCounter()
        {
            switch (_car.GetCurrentState())
            {
                case TeslaState.Drive:
                    _commandCounterDrive++;
                    break;
                case TeslaState.Charge:
                    _commandCounterCharging++;
                    break;
                default:
                    _commandCounterOnline++;
                    break;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
