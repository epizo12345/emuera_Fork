#if R0_B1
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI;

namespace MinorShift.Emuera;
static partial class Program
{
    private static int RunR0B1(string root,string phase)
    {
        root=Path.GetFullPath(root);
        if(!(Path.GetFileName(root).StartsWith("CompactReplacementR0B1_",StringComparison.Ordinal)||Path.GetFileName(root).StartsWith("CompactReplacementR0B2_",StringComparison.Ordinal)||Path.GetFileName(root).StartsWith("CompactReplacementR0C_",StringComparison.Ordinal)||Path.GetFileName(root).StartsWith("CompactReplacementR0C2_",StringComparison.Ordinal)||Path.GetFileName(root).StartsWith("CompactReplacementR0D1_",StringComparison.Ordinal)||Path.GetFileName(root).StartsWith("CompactReplacementR0D2_",StringComparison.Ordinal))||!System.Text.RegularExpressions.Regex.IsMatch(phase,"^(inspect|benchmark|b2inspect|b2benchmark|r0cmanifest|r0cselftest|r0cdeterminism|r0c2benchmark|r0d1benchmark|r0d1selftest|r0d1reloadall|r0d1reloadpartial|r0d1reloadfolder|r0d2matrix|r0d2benchmark|r0d2regression|r0d2reloadall|r0d2reloadpartial|r0d2reloadfolder)-[0-9]+$"))return 64;
        var output=Path.Combine(root,"raw",phase);
        if(Directory.GetFiles(Path.Combine(root,"raw"),phase+"*").Length!=0)return 65;
        B1Proof.Active=true;
#if R0_C
        ConfigureR0CHeadless(root,phase);
#endif
        try
        {
            SetDirPaths(Path.Combine(root,"Data"));
            ConfigData.Instance.LoadConfig();JSONConfig.Load();
            ApplicationConfiguration.Initialize();
            using var window=new Forms.MainWindow([]);
            SynchronizationContext.SetSynchronizationContext(null);
            Preload.Load(ErbDir).GetAwaiter().GetResult();Preload.Load(CsvDir).GetAwaiter().GetResult();
            FontFactory.LoadFontFolder();GlobalStatic.Console=window.R0B1Console;
            var process=new GameProc.Process(window.R0B1Console);GlobalStatic.Process=process;
            using(var log=new StreamWriter(new FileStream(output+".init.log",FileMode.CreateNew)))
                if(!process.Initialize(log).GetAwaiter().GetResult())throw new InvalidOperationException("B1 headless initialization failed");
            Preload.Clear();
#if R0_C
            if(phase.StartsWith("r0cdeterminism-",StringComparison.Ordinal))
            {
                if(!DifferentialDeterminism.Enabled||NextRuntimeMode||NextRuntimeDifferentialCapturePath!=null)
                    throw new InvalidOperationException("R0-C deterministic isolation invariant failed");
                var random=new[]{process.VEvaluator.GetNextRand(1_000_000),process.VEvaluator.GetNextRand(1_000_000),process.VEvaluator.GetNextRand(1_000_000)};
                static long GetTime(DateTime now)=>((((((long)now.Year*100+now.Month)*100+now.Day)*100+now.Hour)*100+now.Minute)*100+now.Second)*1000+now.Millisecond;
                var statementGetTime=DifferentialDeterminism.Now();
                var methodGetTime=DifferentialDeterminism.Now();
                var getTimes=DifferentialDeterminism.Now();
                var getMilliseconds=DifferentialDeterminism.Now();
                var getSeconds=DifferentialDeterminism.Now();
                B1Proof.WriteJson(output+".json",new
                {
                    Schema="emuera-r0c-determinism-selftest-v1",
                    Seed=NextRuntimeDifferentialSeed,
                    ClockBase=NextRuntimeDifferentialClockBase,
                    ClockStepMilliseconds=NextRuntimeDifferentialClockStepMs,
                    VariableEvaluatorRandomSequence=random,
                    DateTimeApiSequence=new
                    {
                        StatementGetTime=GetTime(statementGetTime),
                        MethodGetTime=GetTime(methodGetTime),
                        GetTimes=getTimes.ToString("yyyy/MM/dd HH:mm:ss"),
                        GetMilliseconds=getMilliseconds.Ticks/TimeSpan.TicksPerMillisecond,
                        GetSeconds=getSeconds.Ticks/TimeSpan.TicksPerSecond
                    },
                    NextRuntimeEnabled=false,
                    DifferentialCaptureEnabled=false
                });
                return 0;
            }
#endif
            // Existing codec, direct stream entry: no game-load events and no SQL/temp_db filesystem mutation.
#if R0_D2
            if (phase.StartsWith("r0d2reload", StringComparison.Ordinal))
            {
                B1Proof.WriteJson(output+".json", process.RunR0D2Reload(phase));
                if(window.Visible||B1Proof.BridgeAttempts!=0)throw new InvalidOperationException("GUI/bridge invariant failed");
                return 0;
            }
#endif
#if R0_D1
            // Reload invalidation concerns source/host lifetime, not save decoding or game execution.
            if (phase.StartsWith("r0d1reload", StringComparison.Ordinal))
            {
                B1Proof.WriteJson(output+".json", process.RunR0D1ReloadTest(phase));
                if(window.Visible||B1Proof.BridgeAttempts!=0)throw new InvalidOperationException("GUI/bridge invariant failed");
                return 0;
            }
#endif
            var save=Path.Combine(root,"Data","sav","save219.sav");
            using(var fs=new FileStream(save,FileMode.Open,FileAccess.Read))
            using(var reader=EraBinaryDataReader.CreateReader(fs))
            {
                if(reader!=null)process.VEvaluator.LoadFromStreamBinary(reader);
                else{using var textReader=new EraDataReader(fs);process.VEvaluator.LoadFromStream(textReader);}
            }
            object result;
#if R0_B2
            var stateBefore=process.GetBenchmarkStateHash();
#if R0_D2
            if (phase.StartsWith("r0d2matrix-",StringComparison.Ordinal)) result=process.RunR0D2Matrix(root,phase);
            else if (phase.StartsWith("r0d2benchmark-",StringComparison.Ordinal)) result=process.RunR0D2Benchmark(root,phase);
            else if (phase.StartsWith("r0d2regression-",StringComparison.Ordinal)) result=process.RunR0D2Regression(root,phase);
            else
#endif
            if(phase.StartsWith("b2",StringComparison.Ordinal))result=process.RunB2(root,phase);
#if R0_C
            else if(phase.StartsWith("r0cmanifest-",StringComparison.Ordinal))result=process.WriteR0CManifest(root);
            else if(phase.StartsWith("r0cselftest-",StringComparison.Ordinal))result=process.RunR0CIntegrationSelfTest(root,phase);
            else if(phase.StartsWith("r0c2benchmark-",StringComparison.Ordinal))result=process.RunR0C2Benchmark(root,phase);
#if R0_D1
            else if(phase.StartsWith("r0d1benchmark-",StringComparison.Ordinal))result=process.RunR0D1Benchmark(root,phase);
            else if(phase.StartsWith("r0d1selftest-",StringComparison.Ordinal))result=process.RunR0D1SelfTest(root,phase);
#endif
#endif
            else
#endif
                result=process.RunB1(root,phase);
#if R0_B2
            var stateAfter=process.GetBenchmarkStateHash();
            B1Proof.WriteJson(output+".state.json",new{Before=stateBefore,After=stateAfter,Unchanged=stateBefore==stateAfter,Scope="SHA256 of native normal binary-save serialization; native per-function scratch/stack checked separately; nonserialized globals excluded and inaccessible to candidate writes"});
            if(stateBefore!=stateAfter)throw new InvalidOperationException("Phase save-state hash changed");
#endif
            B1Proof.WriteJson(output+".json",result);
            if(window.Visible||B1Proof.BridgeAttempts!=0)throw new InvalidOperationException("GUI/bridge invariant failed");
            return 0;
        }
        catch(Exception ex)
        {
            using var log=new StreamWriter(new FileStream(output+".failure.txt",FileMode.CreateNew));log.Write(ex);
            return 1;
        }
    }
}
#endif
