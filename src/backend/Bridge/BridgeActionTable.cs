// language: C# 12, file: BridgeActionTable.cs, target: Windows 11 / .NET 8, noyau testable du dispatch des actions du pont
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
namespace PanneauxGlass;

// Table nom -> gestionnaire, construite par réflexion sur les méthodes [BridgeAction]. Générique et sans dépendance au bureau :
// le pont réel l'instancie avec QtShellBridge, les tests avec un propriétaire factice.
internal sealed class BridgeActionTable<TOwner> where TOwner : class
{
    readonly IReadOnlyDictionary<string, Func<TOwner, BridgeCall, Task>> handlers;

    BridgeActionTable(IReadOnlyDictionary<string, Func<TOwner, BridgeCall, Task>> handlers) => this.handlers = handlers;

    internal IReadOnlyCollection<string> Names => handlers.Keys.ToArray();

    // On balaie tous les modificateurs (statique, publique comprises) : une méthode marquée mais hors du contrat serait sinon
    // ignorée sans bruit et l'action disparaîtrait. Toute anomalie lève ici, donc dès la construction du pont.
    internal static BridgeActionTable<TOwner> Build(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;
        var table = new Dictionary<string, Func<TOwner, BridgeCall, Task>>(StringComparer.Ordinal);
        foreach (var method in type.GetMethods(all))
        {
            var names = method.GetCustomAttributes<BridgeActionAttribute>().Select(a => a.Name).ToArray();
            if (names.Length == 0)
                continue;
            string where = type.Name + "." + method.Name;
            var parameters = method.GetParameters();
            if (method.IsStatic || method.IsGenericMethodDefinition || parameters.Length != 1
                || parameters[0].ParameterType != typeof(BridgeCall) || parameters[0].IsOut || parameters[0].ParameterType.IsByRef)
                throw new InvalidOperationException("Signature de [BridgeAction] non prise en charge : " + where
                    + " (attendu : méthode d'instance (BridgeCall)).");
            Func<TOwner, BridgeCall, Task> handler;
            if (method.ReturnType == typeof(Task))
                handler = method.CreateDelegate<Func<TOwner, BridgeCall, Task>>();
            else if (method.ReturnType == typeof(void))
            {
                // Un async void échapperait à l'await du dispatch : l'exception partirait sur le thread du pool au lieu du notice.
                if (method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() != null)
                    throw new InvalidOperationException("[BridgeAction] async void interdit (renvoyer Task) : " + where);
                var sync = method.CreateDelegate<Action<TOwner, BridgeCall>>();
                handler = (owner, call) =>
                {
                    sync(owner, call);
                    return Task.CompletedTask;
                };
            }
            else
                throw new InvalidOperationException("[BridgeAction] doit renvoyer void ou Task : " + where);
            foreach (string name in names)
                if (!table.TryAdd(name, handler))
                    throw new InvalidOperationException("Action de pont en double : « " + name + " » (" + where + ").");
        }
        return new BridgeActionTable<TOwner>(table);
    }

    // Une commande inconnue est ignorée : la réponse porte simplement l'état courant.
    internal Task Dispatch(TOwner owner, string command, BridgeCall call)
        => handlers.TryGetValue(command, out var handler) ? handler(owner, call) : Task.CompletedTask;

    // Exécute l'action et renvoie la réponse brute éventuelle (sondes rapides) ; null = il faut assembler la réponse d'état.
    // Une exception du gestionnaire devient le notice, et les sorties déjà posées dans `call` survivent. La réponse brute
    // posée avant l'exception est écartée : l'appelant doit alors voir l'état et le notice.
    internal async Task<string?> Run(TOwner owner, string command, BridgeCall call, Action<string> log)
    {
        try
        {
            await Dispatch(owner, command, call);
            return call.RawReply;
        }
        catch (Exception ex)
        {
            call.Notice = ex.Message;
            log("Commande Qt: " + ex.Message);
            return null;
        }
    }
}
