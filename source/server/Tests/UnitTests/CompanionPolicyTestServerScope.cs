using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;

namespace DOL.UnitTests;

internal sealed class CompanionPolicyTestServerScope : IDisposable
{
    private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, CompanionPolicyEmptyReadDatabase>();
    private readonly GameServer _previousServer;

    public CompanionPolicyTestServerScope()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
    }

    public void Dispose() => GameServer.LoadTestDouble(_previousServer);

    private sealed class Server : GameServer
    {
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

}

public class CompanionPolicyEmptyReadDatabase : DispatchProxy
{
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
            throw new InvalidOperationException($"Unexpected database write in companion policy test: {method.Name}");

        Type returnType = method.ReturnType;
        return returnType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(returnType)
            ? Activator.CreateInstance(typeof(List<>).MakeGenericType(returnType.GetGenericArguments()[0]))
            : null;
    }
}
