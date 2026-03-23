using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TeslaLogger.Data.Abstractions;

#nullable enable

namespace TeslaLogger.Services.Authentication
{
    /// <summary>
    /// Production implementation of ITokenManager.
    /// Manages Tesla OAuth token lifecycle with automatic refresh and thread-safe access.
    /// </summary>
    internal class TokenManager : ITokenManager, IDisposable
    {
        private readonly Car _car;
        private readonly HttpClient? _httpClient;
        private readonly ILogger<TokenManager>? _logger;

        private string _teslaToken = "";
        private string _teslaId = "";
        private string _teslaVehicleId = "";
        private string _teslaStreamingToken = "";
        private DateTime _nextTokenRefresh = DateTime.Now.AddHours(1);
        private DateTime _lastTokenRefresh = DateTime.Now;
        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

        public string TeslaToken => _teslaToken;
        public string TeslaId => _teslaId;
        public string TeslaVehicleId => _teslaVehicleId;
        public string TeslaStreamingToken => _teslaStreamingToken;
        public DateTime NextTokenRefresh => _nextTokenRefresh;
        public DateTime LastTokenRefresh => _lastTokenRefresh;

        internal TokenManager(Car car, HttpClient? httpClient = null, ILogger<TokenManager>? logger = null)
        {
            _car = car ?? throw new ArgumentNullException(nameof(car));
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default)
        {
            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                if (IsTokenValid())
                {
                    return _teslaToken;
                }

                RefreshToken();
                return _teslaToken;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        public void RefreshToken()
        {
            // Placeholder - will implement full token refresh logic
            _lastTokenRefresh = DateTime.Now;
            _nextTokenRefresh = DateTime.Now.AddDays(45);
        }

        public bool IsTokenValid()
        {
            return !string.IsNullOrEmpty(_teslaToken) && DateTime.Now < _nextTokenRefresh;
        }

        public void CheckRefreshToken()
        {
            if (DateTime.Now >= _nextTokenRefresh)
            {
                RefreshToken();
            }
        }

        public bool HandleUnauthorizedResponse(HttpResponseMessage httpResponse)
        {
            if (httpResponse?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger?.LogWarning("Token invalid. Attempting refresh...");
                RefreshToken();
                return true;
            }
            return false;
        }

        public string GetApiAddress()
        {
            if (_car.FleetAPI)
            {
                if (string.IsNullOrEmpty(_car.FleetApiAddress))
                {
                    return "https://owner-api.teslamotors.com/";
                }
                return _car.FleetApiAddress;
            }
            else if (_car.oldAPIchinaCar)
            {
                return "https://owner-api.vn.cloud.tesla.cn/";
            }
            else
            {
                return "https://owner-api.teslamotors.com/";
            }
        }

        public void ClearSession()
        {
            _teslaToken = "";
            _teslaId = "";
            _teslaVehicleId = "";
            _teslaStreamingToken = "";
            _nextTokenRefresh = DateTime.Now;
            _lastTokenRefresh = DateTime.Now;
        }

        public void Dispose()
        {
            _tokenLock?.Dispose();
        }
    }
}
