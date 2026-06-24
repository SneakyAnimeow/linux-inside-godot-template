using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.JavaScript.NodeApi;
using RealHackerEvolution.Node;

namespace RealHackerEvolution;

public partial class Program : Godot.Node, IAsyncDisposable
{
    public Action<string> OnOutputReceived;
    private JSReference _sendFnRef;
    private NodeJsContext _context;
    private bool _vmReady;

    // Called when the node enters the scene tree for the first time.
    public override async void _Ready()
    {
        _context = new NodeJsContext();

        var workingDir = Path.Combine(AppContext.BaseDirectory, "js");
        var scriptPath = Path.Combine(workingDir, "index.cjs");

        Console.WriteLine("Initiating JS v86 emulator in embedded Node 20...");

        // Fire-and-forget: RunAsync must never complete so the Node.js event loop stays alive
        _ = _context.Runtime.RunAsync(async () =>
        {
            var global = JSValue.Global;

            // 1. Load Module
            var jsModule = global.CallMethod("require", scriptPath);

            var onOutput = JSValue.CreateFunction("onOutput", (args) =>
            {
                var outputChar = (string)args[0];
                OnOutputReceived?.Invoke(outputChar);
                return JSValue.Undefined;
            });

            // 2. Call Function -> Returns a JS Promise
            var promise = jsModule.CallMethod("createVM", onOutput);

            // 3. Resolve callback captures the VM references
            var onResolved = JSValue.CreateFunction("resolve", (args) =>
            {
                var vmInstance = args[0];
                _sendFnRef = new JSReference(vmInstance.GetProperty("send"));
                _vmReady = true;
                Console.WriteLine("VM is ready — send function captured.");
                return JSValue.Undefined;
            });

            var onRejected = JSValue.CreateFunction("reject", (args) =>
            {
                Console.Error.WriteLine($"VM creation failed: {args[0]}");
                return JSValue.Undefined;
            });

            promise.CallMethod("then", onResolved, onRejected);

            // 4. Block forever so the Node.js event loop stays alive
            //    (the v86 emulator runs on timers/listeners inside the event loop)
            await new TaskCompletionSource<bool>().Task;
        });
    }

    public void SendToVm(string data)
    {
        if (!_vmReady || _sendFnRef == null || _context.Runtime == null) return;
        _context.Runtime.Run(() => {
            var sendFn = _sendFnRef.GetValue();
            if (sendFn.IsFunction())
            {
                sendFn.Call(JSValue.Undefined, data);
            }
        });
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_sendFnRef is IAsyncDisposable sendFnRefAsyncDisposable)
            await sendFnRefAsyncDisposable.DisposeAsync();
        else
            _sendFnRef.Dispose();
        await _context.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }
}