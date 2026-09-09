using System.Text.Json;
using Microsoft.JSInterop;
using Faith.UI.Contracts;

namespace Faith.UI.Services;

public interface IClientApi
{
  Task<T> GetAsync<T>(string path);
  Task<ApiReply> PostAsync(string path, object payload);
  Task<ApiReply> PasskeySignInAsync(string email);
  Task<ApiReply> AddPasskeyAsync();
  Task SignOutAsync();
}

public sealed class BrowserClientApi(IJSRuntime js) : IClientApi
{
  private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
  public async Task<T> GetAsync<T>(string path) => JsonSerializer.Deserialize<T>(await js.InvokeAsync<string>("starter.request", "GET", path, null), Options)!;
  public async Task<ApiReply> PostAsync(string path, object payload) => JsonSerializer.Deserialize<ApiReply>(await js.InvokeAsync<string>("starter.request", "POST", path, payload), Options)!;
  public async Task<ApiReply> PasskeySignInAsync(string email) => JsonSerializer.Deserialize<ApiReply>(await js.InvokeAsync<string>("starter.passkey", false, email), Options)!;
  public async Task<ApiReply> AddPasskeyAsync() => JsonSerializer.Deserialize<ApiReply>(await js.InvokeAsync<string>("starter.passkey", true, ""), Options)!;
  public async Task SignOutAsync()
  {
    await js.InvokeVoidAsync("starter.signOut");
  }
}
