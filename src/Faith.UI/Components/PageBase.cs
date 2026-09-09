using Microsoft.AspNetCore.Components;
using Faith.UI.Contracts;
using Faith.UI.Services;

namespace Faith.UI.Components;

public class PageBase : ComponentBase
{
  [Inject] protected IClientApi Api { get; set; } = null!;
  [Inject] protected NavigationManager Navigation { get; set; } = null!;
  protected bool Busy { get; set; }
  protected string Message { get; set; } = "";
  protected bool Success { get; set; }
  protected async Task Run(Func<Task<ApiReply>> action)
  {
    if (Busy) return;
    Busy = true;
    Message = "";
    try { var reply = await action(); Success = reply.Success; Message = reply.Message; }
    catch (Exception) { Success = false; Message = "We couldn't complete that request. Check your connection and try again."; }
    finally { Busy = false; }
  }
  protected async Task Load(Func<Task> action)
  {
    try { await action(); }
    catch (Exception) { Message = "This content is unavailable. Sign in if required, or try again."; }
    StateHasChanged();
  }
}
