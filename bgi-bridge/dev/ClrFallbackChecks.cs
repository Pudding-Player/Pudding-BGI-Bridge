// 本体方法签名镜像来自 UseRedemptionCodeTask；只替换游戏副作用边界为计数，不登记兑换专用适配。
using System.Text;
using System.IO;
using System.Text.Json;
using BgiBridge.Bgi;
using BgiBridge.Catalog;
using BgiBridge.Protocol;
using BgiBridge.Tools;

namespace BgiBridge.Dev
{
    public static class Recorder
    {
        public static int Constructors,Calls,RunnerCalls;public static bool TokenInjected,LockHeld;
        public static readonly List<string> Codes=[];
    }
    static class Driver
    {
        static readonly MethodRegistry Registry=new();
        static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
        static readonly List<object> Requests=[];
        static readonly Dictionary<string,object> Jobs=[];
        static readonly string Instance=Guid.NewGuid().ToString("N"),Version=Guid.NewGuid().ToString("N");
        static bool uiAttempted;
        static string root="";
        static async Task Main(string[] args)
        {
            Console.InputEncoding=Encoding.UTF8;Console.OutputEncoding=new UTF8Encoding(false);
            ClrFallbackTools.Register(Registry);
            NativeUiTools.Register(Registry); // 保留可发现的 UI 元数据，但执行端显式禁止，验收不能绕行。
            if(args.Length==0||args[0]=="--self-check") {await SelfCheck();return;}
            root=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.Combine(root,"User"));
            Console.WriteLine(JsonSerializer.Serialize(new{ready=true,pid=Environment.ProcessId,originalType=typeof(BetterGenshinImpact.GameTask.UseRedeemCode.UseRedemptionCodeTask).FullName,dedicatedRegistrationPresent=false,uiEnabled=false},Json));
            while(await Console.In.ReadLineAsync() is {} line)
            {
                try {var request=JsonDocument.Parse(line.TrimStart('\uFEFF')).RootElement.Clone();var value=await Handle(request);Console.WriteLine(JsonSerializer.Serialize(new{status=200,value},Json));}
                catch(BridgeException e){Console.WriteLine(JsonSerializer.Serialize(new{status=e.HttpStatus,value=new{code=e.Code,message=e.Message}},Json));}
                catch(Exception e){Console.WriteLine(JsonSerializer.Serialize(new{status=500,value=new{code="FIXTURE_ERROR",message=e.Message}},Json));}
            }
        }
        static object Describe(MethodDescriptor d)=>new{methodId=d.Id,displayName=d.Guide.Title,group=d.Group,instanceId=Instance,summary=d.Guide.Purpose,guide=d.Guide,inputSchema=d.InputSchema,outputSchema=d.OutputSchema,effect=d.Effect,executionMode=d.ReadOnly?"inline":"job",concurrency="gameExclusive",cancellation="cooperative",requiresConfirmation=!d.ReadOnly,callable=true,catalogVersion=Version};
        static async Task<object> Handle(JsonElement a)
        {
            var path=a.GetProperty("path").GetString()!;var uri=new Uri("http://127.0.0.1"+path);
            var b=a.TryGetProperty("body",out var supplied)&&supplied.ValueKind==JsonValueKind.Object?supplied:ArgumentSchema.Empty;
            Requests.Add(new{method=a.GetProperty("method").GetString(),path,body=b});
            if(uri.AbsolutePath=="/fixture/state")return new{originalMethodCalls=Recorder.Calls,constructors=Recorder.Constructors,runnerCalls=Recorder.RunnerCalls,tokenInjected=Recorder.TokenInjected,lockHeld=Recorder.LockHeld,codes=Recorder.Codes.ToArray(),uiAttempted,requests=Requests.ToArray(),actualGameActions=0};
            if(uri.AbsolutePath=="/bridge/v1/info")return new{protocolVersion="1",instanceId=Instance,catalogVersion=Version,features=new[]{"catalog","invoke","jobs","state"},isolated=true};
            if(uri.AbsolutePath=="/bridge/v1/host")return new{userPath=Path.Combine(root,"User"),isolated=true};
            // 只模拟执行前提；本体方法已替换为记录边界，不调用任何真实游戏逻辑。
            if(uri.AbsolutePath=="/bridge/v1/state")return new{instanceId=Instance,snapshotId=Guid.NewGuid().ToString("N"),observedAt=DateTimeOffset.UtcNow,gameReady=true,runtime=new{hostLoaded=true,captureReady=true,inMainUi=true,gameProcessRunning=true,windowActive=true,gameHandle=0,isolated=true,simulatedReadiness=true}};
            if(uri.AbsolutePath=="/bridge/v1/catalog")
            {
                var q=System.Web.HttpUtility.ParseQueryString(uri.Query);var offset=int.TryParse(q["offset"],out var o)?o:0;var limit=int.TryParse(q["limit"],out var l)?l:8;
                var all=Registry.Search(q["q"],int.MaxValue).Where(m=>string.IsNullOrEmpty(q["group"])||q["group"]==m.Group).ToArray();
                return new{total=all.Length,offset,items=all.Skip(offset).Take(limit).Select(m=>m.Discovery(true,null,Version)),catalogVersion=Version,nextOffset=offset+limit<all.Length?(int?)(offset+limit):null,
                    gapId=all.Length==0&&!string.IsNullOrWhiteSpace(q["q"])?ClrFallback.RecordGap("当前常规接口未命中："+q["q"]):null,
                    hint=all.Length==0&&!string.IsNullOrWhiteSpace(q["q"])?ClrFallback.MissingHint:null};
            }
            if(uri.AbsolutePath.StartsWith("/bridge/v1/catalog/"))
            {
                var id=Uri.UnescapeDataString(uri.AbsolutePath["/bridge/v1/catalog/".Length..]);
                if(id.StartsWith("bgi.ui.")){uiAttempted=true;throw BridgeException.InvalidArgument("本次测试禁止 UI。");}
                if(!Registry.TryGet(id,out var d,out _))throw BridgeException.NotFound("当前反射适配未登记此接口，本体类型仍然加载。");return Describe(d);
            }
            if(uri.AbsolutePath=="/bridge/v1/invoke")
            {
                var id=b.GetProperty("methodId").GetString()!;
                if(id.StartsWith("bgi.ui.")){uiAttempted=true;throw BridgeException.InvalidArgument("本次测试禁止 UI。");}
                if(!Registry.TryGet(id,out var d,out var handler))throw BridgeException.NotFound("当前反射适配未登记此接口。");
                var arguments=b.GetProperty("arguments");ArgumentSchema.Validate(arguments,d.InputSchema);
                var before=Recorder.Calls;var result=await handler(arguments,CancellationToken.None);
                if(d.ReadOnly)return new{methodId=id,result};
                var jobId=Guid.NewGuid().ToString("N");var job=new{jobId,methodId=id,state="completed",result,verification=new{status=Recorder.Calls==before+1?"succeeded":"unknown",scope="isolatedMethodBoundary",reason="只核验隔离本体原方法被调用一次，未执行真实游戏兑换。",actualGameActions=0}};Jobs[jobId]=job;return job;
            }
            if(uri.AbsolutePath.StartsWith("/bridge/v1/jobs/"))return Jobs[uri.AbsolutePath["/bridge/v1/jobs/".Length..]];
            throw BridgeException.NotFound("隔离宿主没有此端点。");
        }
        static async Task SelfCheck()
        {
            var originalSession=ClrFallback.BeginSession();
            if(Registry.TryGet("cmd.task_settings_page.switch_auto_redeem_code",out _,out _))throw new Exception("专用适配不能存在");
            var type=typeof(BetterGenshinImpact.GameTask.UseRedeemCode.UseRedemptionCodeTask);
            var search=JsonSerializer.SerializeToElement(ClrFallback.Search("UseRedemptionCodeTask",0,8),Json);
            ArgumentSchema.Validate(search,AgentSchemas.Output("bgi.clr.search",""));
            var description=JsonSerializer.SerializeToElement(ClrFallback.Describe(type.FullName!),Json);
            ArgumentSchema.Validate(description,AgentSchemas.Output("bgi.clr.describe",""));
            var method=description.GetProperty("methods").EnumerateArray().Single(m=>m.GetProperty("name").GetString()=="Start");
            var ctor=description.GetProperty("constructors").EnumerateArray().Single(c=>c.GetProperty("canPrepare").GetBoolean());
            var plan=JsonSerializer.SerializeToElement(ClrFallback.Prepare(JsonSerializer.SerializeToElement(new{gapId=ClrFallback.RecordGap("专用适配未登记"),memberId=method.GetProperty("memberId").GetString(),constructorId=ctor.GetProperty("memberId").GetString(),constructorArguments=new{strList=new[]{"GAPTEST123456"}},arguments=new{},purpose="验证漏登记后直接调用仍存在的本体方法"})),Json);
            if(Recorder.Constructors!=0)throw new Exception("预览不能构造业务对象");
            ArgumentSchema.Validate(plan,AgentSchemas.Output("bgi.clr.prepare",""));
            if(plan.GetProperty("agentCall").GetProperty("methodId").GetString()!="bgi.clr.execute"||plan.GetProperty("agentCall").GetProperty("arguments").GetProperty("planId").GetString()!=plan.GetProperty("planId").GetString())throw new Exception("精确执行映射缺失");
            var executed=JsonSerializer.SerializeToElement(await ClrFallback.Execute(plan.GetProperty("planId").GetString()!,CancellationToken.None),Json);
            ArgumentSchema.Validate(executed,AgentSchemas.Output("bgi.clr.execute",""));
            if(Recorder.Calls!=1||Recorder.RunnerCalls!=1||!Recorder.LockHeld||!Recorder.TokenInjected||Recorder.Codes.Single()!="GAPTEST123456")throw new Exception("本体链路没有闭合");
            bool reused=false;try{await ClrFallback.Execute(plan.GetProperty("planId").GetString()!,CancellationToken.None);}catch(BridgeException){reused=true;}
            if(!reused||Recorder.Calls!=1)throw new Exception("一次性计划被重复执行");
            var pending=JsonSerializer.SerializeToElement(ClrFallback.Prepare(JsonSerializer.SerializeToElement(new{gapId=ClrFallback.RecordGap("仍缺少专用适配"),memberId=method.GetProperty("memberId").GetString(),constructorId=ctor.GetProperty("memberId").GetString(),constructorArguments=new{strList=new[]{"GAPTEST123456"}},arguments=new{},purpose="验证未使用计划在重连后失效"})),Json);
            ClrFallback.BeginSession();bool lateRejected=false;
            using(ClrFallback.Scope(originalSession)){try{ClrFallback.RecordGap("迟到的旧会话");}catch(BridgeException){lateRejected=true;}}
            bool reconnectRejected=false;try{await ClrFallback.Execute(pending.GetProperty("planId").GetString()!,CancellationToken.None);}catch(BridgeException){reconnectRejected=true;}
            if(!lateRejected||!reconnectRejected||Recorder.Calls!=1)throw new Exception("重连后凭据或迟到回调没有被拒绝");
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,dedicatedAdapterAbsent=true,originalType=type.FullName,method="Start",constructorArgument="strList",nativeMethodCalls=Recorder.Calls,runnerCalls=Recorder.RunnerCalls,tokenInjected=Recorder.TokenInjected,uiOperations=0,actualGameActions=0,duplicateRejected=reused,reconnectRejected,lateRejected},Json));
        }
    }
}
namespace BetterGenshinImpact.GameTask
{
    public interface ISoloTask {string Name{get;}Task Start(CancellationToken ct);}
    public sealed class TaskRunner
    {
        internal static readonly SemaphoreSlim Gate=new(1,1);
        public async Task RunThreadAsync(Func<Task> action){if(!await Gate.WaitAsync(0))return;try{BgiBridge.Dev.Recorder.RunnerCalls++;BetterGenshinImpact.Core.Script.CancellationContext.Instance().Set();await Task.Run(action);}finally{Gate.Release();}}
    }
}
namespace BetterGenshinImpact.Core.Script
{
    public sealed class CancellationContext {static readonly CancellationContext Value=new();public static CancellationContext Instance()=>Value;public CancellationTokenSource Cts{get;private set;}=new();public void Set()=>Cts=new();}
}
namespace BetterGenshinImpact.GameTask.UseRedeemCode.Model { public sealed class RedeemCode(string code){public string Code{get;}=code;} }
namespace BetterGenshinImpact.GameTask.UseRedeemCode
{
    public sealed class UseRedemptionCodeTask:ISoloTask
    {
        readonly List<string> values;
        public UseRedemptionCodeTask(List<Model.RedeemCode> list){values=list.Select(v=>v.Code).ToList();BgiBridge.Dev.Recorder.Constructors++;}
        public UseRedemptionCodeTask(List<string> strList){values=strList.Where(v=>!string.IsNullOrWhiteSpace(v)).ToList();BgiBridge.Dev.Recorder.Constructors++;}
        public string Name=>"使用兑换码";
        public Task Start(CancellationToken ct){ct.ThrowIfCancellationRequested();BgiBridge.Dev.Recorder.Calls++;BgiBridge.Dev.Recorder.Codes.AddRange(values);BgiBridge.Dev.Recorder.TokenInjected=ct.CanBeCanceled;BgiBridge.Dev.Recorder.LockHeld=TaskRunner.Gate.CurrentCount==0;return Task.CompletedTask;}
    }
}
