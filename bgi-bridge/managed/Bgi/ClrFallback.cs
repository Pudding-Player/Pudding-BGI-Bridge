using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using BgiBridge.Catalog;
using BgiBridge.Protocol;

namespace BgiBridge.Bgi;

/// <summary>桥漏登记时检查宿主本体；元数据读取不构造对象，执行只接收一次性计划。</summary>
public static class ClrFallback
{
    public const string MissingHint="当前桥未登记不等于宿主本体没有实现。宿主业务缺项先用离线 hostApi 声明定位类型，再发现 bgi.clr.search/describe/prepare/execute 的当前契约，直接调用本体，不转向 UI。用户资源、脚本参数或脚本源码才使用 user/repo 工具；不要穷举中英文关键词。";
    private sealed record Lease(object Value, DateTimeOffset Expires);
    private sealed record Member(MethodBase Method, string Fingerprint);
    private sealed record Plan(Member Call, Member? Constructor, JsonElement Arguments, JsonElement ConstructorArguments, string Gap);
    private static readonly ConcurrentDictionary<string,Lease> Leases=new();
    private static readonly object LeaseGate=new();
    private static readonly AsyncLocal<int?> ExpectedSession=new();
    private static int session;
    public static bool SessionCurrent=>ExpectedSession.Value is null||ExpectedSession.Value==Volatile.Read(ref session);
    public static int BeginSession(){lock(LeaseGate){Leases.Clear();return ++session;}}
    public static void EndSession(int expected){lock(LeaseGate){if(session==expected){Leases.Clear();session++;}}}
    private sealed class SessionScope(int? previous):IDisposable{public void Dispose()=>ExpectedSession.Value=previous;}
    public static IDisposable Scope(int expected){var previous=ExpectedSession.Value;ExpectedSession.Value=expected;return new SessionScope(previous);}
    private const BindingFlags Public=BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
    private static string Put(object value)
    {
        lock(LeaseGate)
        {
        if(!SessionCurrent)throw new BridgeException("STALE_MEMBER","桥会话已变化，不接受迟到的成员或计划。",409);
        foreach(var item in Leases.Where(p=>p.Value.Expires<DateTimeOffset.UtcNow).ToArray())Leases.TryRemove(item.Key,out _);
        if(Leases.Count>=2048)throw BridgeException.Busy("运行时成员凭据过多，请完成已有操作或等待过期。");
        var id=Guid.NewGuid().ToString("N");Leases[id]=new(value,DateTimeOffset.UtcNow.AddMinutes(5));return id;
        }
    }
    private static T Get<T>(string id,bool consume=false)
    {
        lock(LeaseGate)
        {
        if(!SessionCurrent)throw new BridgeException("STALE_MEMBER","桥会话已变化，重新发现本体成员。",409);
        if(!Leases.TryGetValue(id,out var lease)||lease.Expires<DateTimeOffset.UtcNow||lease.Value is not T value)
            throw new BridgeException("STALE_MEMBER","成员／缺口／计划凭据过期或不属于当前桥，重新发现。",409);
        if(consume&&!Leases.TryRemove(id,out _))throw new BridgeException("PLAN_USED","计划已使用，不自动重试。",409);
        return value;
        }
    }
    public static string RecordGap(string observation)=>Put(observation);
    private static string Fingerprint(MethodBase member)=>ValueContract.Version(JsonSerializer.SerializeToElement(new{
        module=member.Module.ModuleVersionId,member.MetadataToken,owner=member.DeclaringType?.FullName,
        parameters=member.GetParameters().Select(p=>new{p.Name,type=p.ParameterType.AssemblyQualifiedName,p.IsOptional}),
        result=(member as MethodInfo)?.ReturnType.AssemblyQualifiedName}));
    private static bool UiType(Type type)=>type.Namespace?.StartsWith("BetterGenshinImpact.View",StringComparison.Ordinal)==true
        ||typeof(System.Windows.DependencyObject).IsAssignableFrom(type);
    private static bool Forbidden(string text)=>new[]{"password","secret","token","credential","cookie","apikey","api_key","shell","process","executecommand","script","download","update","filepath","directory"}
        .Any(word=>text.Contains(word,StringComparison.OrdinalIgnoreCase));
    private static bool Business(Type type)=>type.Assembly.GetName().Name==Reflect.HostAssembly&&!UiType(type)
        &&(type.Namespace?.StartsWith("BetterGenshinImpact.GameTask",StringComparison.Ordinal)==true
           ||type.Namespace?.StartsWith("BetterGenshinImpact.Service",StringComparison.Ordinal)==true)
        &&!Forbidden(type.FullName??"");
    private static string? Restriction(MethodBase method)
    {
        var owner=method.DeclaringType!;
        if(!Business(owner))return "该类型不是可直接调用的宿主业务类型；界面类型、脚本与系统执行入口不作为动态补足。";
        if(method.ContainsGenericParameters||method is MethodInfo{IsSpecialName:true}||method is MethodInfo{ReturnType:var ret}&&ret==typeof(void)&&method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute)))return "开放泛型、属性事件或 async void 不支持动态调用。";
        if(Forbidden(method.Name))return "该成员属于凭据、程序更新或系统／脚本执行范围。";
        if(new[]{"Click","KeyPress","SendKey","Mouse","OpenWindow","ShowWindow","ShowDialog","Navigate"}.Any(word=>method.Name.Contains(word,StringComparison.OrdinalIgnoreCase))
            ||SourceDocumentation.Find("M",owner,method.Name) is { } document&&(document.NeedsDialogInput||document.Interaction=="nativeUiContinuation"))
            return "界面、弹窗或底层键鼠成员不能借本体补足绕过 UI 优先级约束。";
        if(method is MethodInfo info&&info.ReturnType!=typeof(void)&&!typeof(Task).IsAssignableFrom(info.ReturnType)&&ValueContract.Schema(info.ReturnType) is null)
            return "返回类型缺少可等待的 Task 或安全值契约，不释放任务锁后冒充异步调用完成。";
        foreach(var parameter in method.GetParameters())
            if(parameter.ParameterType!=typeof(CancellationToken)&&(parameter.ParameterType.IsByRef||parameter.IsOut||Forbidden(parameter.Name??"")||ValueContract.Schema(parameter.ParameterType) is null))
                return "参数缺少可核验的简单 JSON 契约；不伪造复杂宿主对象。";
        return null;
    }
    private static JsonElement Schema(MethodBase method)=>AgentSchemas.Object(method.GetParameters().Where(p=>p.ParameterType!=typeof(CancellationToken))
        .Select(p=>(p.Name!,ValueContract.Schema(p.ParameterType)??ArgumentSchema.Parse("""{"not":{},"description":"此参数没有受支持的 JSON 契约，不接受 UI objectId 或伪造对象。"}"""),!p.IsOptional)).ToArray());
    private static object Description(MethodBase method)
    {
        var reason=Restriction(method);var member=new Member(method,Fingerprint(method));
        return new{memberId=Put(member),name=method is ConstructorInfo?".ctor":method.Name,
            declaringType=method.DeclaringType!.FullName,signature=method.ToString(),isStatic=method.IsStatic,
            inputSchema=Schema(method),injectedCancellation=method.GetParameters().Any(p=>p.ParameterType==typeof(CancellationToken)),
            canPrepare=reason is null,unavailableReason=reason,documentation=SourceDocumentation.Find("M",method.DeclaringType,method.Name),
            returnType=(method as MethodInfo)?.ReturnType.FullName,source="loadedHostClrMetadata",executionVerified=false};
    }
    public static object Search(string query,int offset,int limit)
    {
        var terms=query.Split([' ','\t'],StringSplitOptions.RemoveEmptyEntries);
        var types=CommandTargets.Types().Where(t=>t.IsPublic&&!t.ContainsGenericParameters&&!UiType(t))
            .Where(t=>terms.Length==0||terms.Any(term=>(t.FullName??"").Contains(term,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t=>t.FullName,StringComparer.Ordinal).ToArray();
        return new{source="loadedHostClrMetadata",total=types.Length,offset,items=types.Skip(offset).Take(Math.Clamp(limit,1,20))
            .Select(t=>new{typeName=t.FullName,canInspect=true,executionCandidate=Business(t),kind="hostType"}),
            nextOffset=offset+limit<types.Length?(int?)(offset+limit):null,
            next="按 typeName 读取当前成员与构造器；中文功能先用离线源码声明定位类型名。检查不构造对象，不能把源码声明当成已调用。"};
    }
    public static object Describe(string typeName,int offset=0,int limit=20)
    {
        var type=Reflect.FindType(typeName)??throw BridgeException.NotFound("宿主尚未加载此类型。");
        if(type.Assembly.GetName().Name!=Reflect.HostAssembly)throw BridgeException.InvalidArgument("只能检查当前宿主类型。");
        if(UiType(type))return new{typeName=type.FullName,source="loadedHostClrMetadata",canPrepare=false,constructors=Array.Empty<object>(),methods=Array.Empty<object>(),unavailableReason="类型属于宿主界面，不提供非 UI 的构造或调用；不读取界面状态。"};
        if(offset<0)throw BridgeException.InvalidArgument("offset 不能为负数。");limit=Math.Clamp(limit,1,30);
        var constructors=type.GetConstructors();var methods=type.GetMethods(Public).Where(m=>!m.IsSpecialName).OrderBy(m=>m.Name,StringComparer.Ordinal).ThenBy(m=>m.MetadataToken).ToArray();
        return new{typeName=type.FullName,source="loadedHostClrMetadata",prepareTool="bgi.api.read",prepareMethodId="bgi.clr.prepare",constructors=constructors.Select(Description),
            methods=methods.Skip(offset).Take(limit).Select(Description),methodTotal=methods.Length,nextMethodOffset=offset+limit<methods.Length?(int?)(offset+limit):null,
            canPrepare=methods.Any(m=>Restriction(m) is null&&(m.IsStatic||constructors.Any(c=>Restriction(c) is null))),
            next="选择真实方法 memberId；实例方法还需选择 canPrepare=true 的构造器 memberId。只有缺口凭据和参数预览通过后才执行，不操作页面。"};
    }
    private static object?[] Arguments(MethodBase method,JsonElement values,CancellationToken cancellation)
    {
        ArgumentSchema.Validate(values,Schema(method));
        return method.GetParameters().Select(p=>p.ParameterType==typeof(CancellationToken)?(object)cancellation:
            values.TryGetProperty(p.Name!,out var value)?JsonSerializer.Deserialize(value.GetRawText(),p.ParameterType,ValueContract.Json):p.DefaultValue).ToArray();
    }
    public static object Prepare(JsonElement args)
    {
        var gap=args.GetProperty("gapId").GetString()!;var observation=Get<string>(gap);
        var member=Get<Member>(args.GetProperty("memberId").GetString()!);
        if(member.Method is not MethodInfo method||Restriction(method) is {} rejected)throw BridgeException.InvalidArgument(member.Method is MethodInfo?Restriction(member.Method)!:"选择方法而非构造器作为调用目标。");
        Member? ctor=null;
        var constructorArgs=args.TryGetProperty("constructorArguments",out var ca)?ca.Clone():ArgumentSchema.Empty;
        if(!method.IsStatic)
        {
            ctor=Get<Member>(args.GetProperty("constructorId").GetString()!);
            if(ctor.Method is not ConstructorInfo||ctor.Method.DeclaringType!=method.DeclaringType||Restriction(ctor.Method) is not null)
                throw BridgeException.InvalidArgument("构造器与当前方法所属类型不一致或不支持。");
            _=Arguments(ctor.Method,constructorArgs,CancellationToken.None);
        }
        var arguments=args.GetProperty("arguments").Clone();_=Arguments(method,arguments,CancellationToken.None);
        if(arguments.GetRawText().Length+constructorArgs.GetRawText().Length>65536)throw BridgeException.InvalidArgument("动态调用参数超过 64 KiB。");
        var purpose=args.GetProperty("purpose").GetString();
        if(string.IsNullOrWhiteSpace(purpose))throw BridgeException.InvalidArgument("需要本次目标和已核对的本体方法语义依据。");
        var runner=Reflect.FindType("BetterGenshinImpact.GameTask.TaskRunner");
        if(runner?.GetMethod("RunThreadAsync",[typeof(Func<Task>)]) is null)throw BridgeException.Missing("当前宿主没有兼容的 TaskRunner，不绕过任务锁执行。");
        var plan=new Plan(member,ctor,arguments,constructorArgs,gap);
        var planId=Put(plan);
        return new{planId,prepared=true,executed=false,expiresInSeconds=300,
            agentCall=new{tool="bgi.api.invoke",methodId="bgi.clr.execute",arguments=new{planId}},preview=new{purpose,gapObservation=observation,
            typeName=method.DeclaringType!.FullName,method=method.ToString(),constructor=ctor?.Method.ToString(),arguments,constructorArguments=constructorArgs,
            execution="approved constructor → native TaskRunner.RunThreadAsync → discovered original method; CancellationToken injected",bridgeUiFallbackUsed=false},
            next="预览符合本轮用户授权后，直接按 agentCall 调用一次；执行器会复核当前契约，不再搜索或猜执行入口。不保存快捷／定时入口，不自动重试。"};
    }
    public static async Task<object?> Execute(string id,CancellationToken cancellation)
    {
        var plan=Get<Plan>(id,consume:true);_=Get<string>(plan.Gap);
        if(plan.Call.Fingerprint!=Fingerprint(plan.Call.Method)||plan.Constructor is {} constructor&&constructor.Fingerprint!=Fingerprint(constructor.Method))
            throw new BridgeException("MEMBER_CHANGED","本体成员签名已变化，本次未执行。",409);
        var method=(MethodInfo)plan.Call.Method;
        if(Restriction(method) is {} restriction)throw BridgeException.InvalidArgument(restriction);
        cancellation.ThrowIfCancellationRequested();
        var runnerType=Reflect.RequireType("BetterGenshinImpact.GameTask.TaskRunner");
        var runner=Activator.CreateInstance(runnerType)!;bool entered=false;object? result=null;Exception? failure=null;
        Func<Task> action=async()=>
        {
            entered=true;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var context=Reflect.Singleton("BetterGenshinImpact.Core.Script.CancellationContext");
                var nativeToken=context is null?cancellation:(Reflect.Get(context,"Cts") as CancellationTokenSource)?.Token??cancellation;
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,nativeToken);
                linked.Token.ThrowIfCancellationRequested();
                var receiver=method.IsStatic?null:((ConstructorInfo)plan.Constructor!.Method).Invoke(Arguments(plan.Constructor.Method,plan.ConstructorArguments,linked.Token));
                result=method.Invoke(receiver,Arguments(method,plan.Arguments,linked.Token));
                if(result is Task task){await task.ConfigureAwait(false);result=task.GetType().IsGenericType?task.GetType().GetProperty("Result")?.GetValue(task):null;}
            }
            catch(Exception error){failure=Reflect.Root(error);throw;}
        };
        var operation=runnerType.GetMethod("RunThreadAsync",[typeof(Func<Task>)])?.Invoke(runner,[action]) as Task
            ??throw BridgeException.Missing("宿主 TaskRunner 契约变化。");
        await operation.ConfigureAwait(false);
        if(!entered)throw BridgeException.Busy("宿主没有接纳本体方法，可能有其他任务持锁。");
        if(failure is OperationCanceledException)throw failure;
        if(failure is not null)throw BridgeException.Failed("本体方法执行异常，可能已有副作用；请核实，不能自动重试："+failure.Message);
        return new{returned=true,typeName=method.DeclaringType!.FullName,method=method.Name,bridgeUiFallbackUsed=false,
            result=result is null||result is bool||result is int?result:null,
            verification=new{status="unknown",scope="hostMethod",reason="原方法已返回，业务效果仍需本次宿主日志或结果证明。没有可用回执源时明确未核验，不重复执行或反复检索不存在的结果入口。"}};
    }
}
