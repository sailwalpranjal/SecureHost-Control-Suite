using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using SecureHostCore.Models;

namespace SecureHostGUI.Services;

public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5555")
        };
    }

    public async Task<ServiceStatus?> GetStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/status");
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ServiceStatus>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<PolicyRule>> GetRulesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/rules");
            if (!response.IsSuccessStatusCode)
                return new List<PolicyRule>();

            return await response.Content.ReadFromJsonAsync<List<PolicyRule>>() ?? new List<PolicyRule>();
        }
        catch
        {
            return new List<PolicyRule>();
        }
    }

    public async Task<bool> AddRuleAsync(PolicyRule rule)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/rules", rule);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateRuleAsync(ulong ruleId, PolicyRule rule)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/api/rules/{ruleId}", rule);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteRuleAsync(ulong ruleId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/rules/{ruleId}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<NetworkConnection>> GetConnectionsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/network/connections");
            if (!response.IsSuccessStatusCode)
                return new List<NetworkConnection>();

            return await response.Content.ReadFromJsonAsync<List<NetworkConnection>>() ?? new List<NetworkConnection>();
        }
        catch
        {
            return new List<NetworkConnection>();
        }
    }

    public async Task<byte[]?> ExportAuditAsync(DateTime startTime, DateTime endTime)
    {
        try
        {
            var url = $"/api/audit/export?startTime={startTime:O}&endTime={endTime:O}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> ToggleRuleAsync(ulong ruleId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/rules/{ruleId}/toggle", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ResetAllDevicesAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/system/reset", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public class ServiceStatus
{
    public string Service { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int RulesCount { get; set; }
    public int ActiveRules { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string Uptime { get; set; } = string.Empty;
}

public class NetworkConnection
{
    public string Protocol { get; set; } = string.Empty;
    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string State { get; set; } = string.Empty;
}
