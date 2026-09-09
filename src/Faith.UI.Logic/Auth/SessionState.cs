using Branches.Core.State;

namespace Faith.UI.Logic.Auth;

public sealed class SessionState : IStateAuthenticationSubject
{
  public Guid? UserId { get; set; }
  public int SecurityVersion { get; set; }
  public bool IsAuthenticatedState => UserId.HasValue;
}
