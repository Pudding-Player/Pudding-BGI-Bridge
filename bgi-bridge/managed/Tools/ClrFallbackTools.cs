using BgiBridge.Bgi;
using BgiBridge.Catalog;

namespace BgiBridge.Tools;

public static class ClrFallbackTools
{
    public static void Register(MethodRegistry registry)
    {
        AgentGuide Guide(string title,string purpose,bool write)=>new(title,purpose,
            ["常规业务接口确实缺项，需要找到宿主仍然存在的原始实现时；不作为常态查询。"],
            ["先查常规接口；缺口凭据来自本轮目录未命中，不从名称猜语义；界面操作不属于本链路。"],
            write?["按预览构造宿主业务对象，并在原生任务执行器内调用本体方法；可能影响游戏或宿主状态。"]:["仅检查当前 CLR 元数据或生成预览，不构造宿主对象。"],
            "类型、签名、参数契约或方法返回；不是业务成功证明。","取得真实参数与来源，预览后按本轮授权执行一次并核对宿主结果。","已发生的原生副作用不能自动回退。",[],"loaded-host-clr-fallback");
        var text=AgentSchemas.Text("本轮真实发现的标识。",256);
        var obj=ArgumentSchema.Parse("""{"type":"object","maxProperties":32}""");
        registry.Register("bgi.clr.search","runtime","缺项补足：检索宿主已加载的非界面 CLR 类型",(a,_)=>Task.FromResult<object?>(ClrFallback.Search(a.GetProperty("query").GetString()!,a.TryGetProperty("offset",out var o)?o.GetInt32():0,a.TryGetProperty("limit",out var l)?l.GetInt32():8)),
            inputSchema:AgentSchemas.Object(("query",text,true),("offset",ArgumentSchema.Parse("""{"type":"integer","minimum":0}"""),false),("limit",ArgumentSchema.Parse("""{"type":"integer","minimum":1,"maximum":20}"""),false)),guide:Guide("发现宿主本体类型","专用反射登记缺失时，按源码中的类型名、命名空间或符号检索宿主本体；不用 UI 反射代替。",false));
        registry.Register("bgi.clr.describe","runtime","检查宿主本体方法与构造器的真实参数契约",(a,_)=>Task.FromResult<object?>(ClrFallback.Describe(a.GetProperty("typeName").GetString()!,a.TryGetProperty("offset",out var o)?o.GetInt32():0,a.TryGetProperty("limit",out var l)?l.GetInt32():20)),
            inputSchema:AgentSchemas.Object(("typeName",text,true),("offset",ArgumentSchema.Parse("""{"type":"integer","minimum":0}"""),false),("limit",ArgumentSchema.Parse("""{"type":"integer","minimum":1,"maximum":30}"""),false)),guide:Guide("读取本体成员契约","按 CLR 类型名分页读取当前已加载的公共方法和构造器，区分可准备的简单参数重载和不可调用成员。",false));
        registry.Register("bgi.clr.prepare","runtime","为漏适配的本体方法生成参数和构造器预览",(a,_)=>Task.FromResult<object?>(ClrFallback.Prepare(a)),
            inputSchema:AgentSchemas.Object(("gapId",AgentSchemas.Text("常规接口目录未命中时返回的真实 gapId，不自行生成。",256),true),
                ("memberId",AgentSchemas.Text("describe.methods 中选中方法的 memberId。",256),true),
                ("constructorId",AgentSchemas.Text("实例方法必需：describe.constructors 中 canPrepare=true 的构造器 memberId；静态方法省略。",256),false),
                ("constructorArguments",ArgumentSchema.Parse("""{"type":"object","maxProperties":32,"description":"严格满足选中构造器的 inputSchema；参数名和类型来自当前元数据。"}"""),false),
                ("arguments",ArgumentSchema.Parse("""{"type":"object","maxProperties":32,"description":"严格满足选中方法的 inputSchema；取消令牌由执行器注入，不填写。"}"""),true),
                ("purpose",AgentSchemas.Text("本次用户目标、成员语义及源码／文档依据。",2000),true)),guide:Guide("预览本体调用","绑定真实缺口、成员、构造器与参数；准备不构造对象、不执行本体。",false));
        registry.Register("bgi.clr.execute","runtime","一次性调用已发现并预览的宿主本体业务方法",(a,c)=>ClrFallback.Execute(a.GetProperty("planId").GetString()!,c),readOnly:false,
            inputSchema:AgentSchemas.Object(("planId",text,true)),guide:Guide("调用本体方法","使用本次一次性计划，经过原生 TaskRunner 的锁、初始化和取消链路调用原方法；禁止 UI 绕行、保存快捷／定时入口及自动重试。",true),requiresGameReady:true,ownsHostCancellation:true);
    }
}
