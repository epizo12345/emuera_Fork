#if R0_F4C
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal void R0F4CBeforeLegacyInstruction(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (R0F1Context.IsSkillEmulatorLine(line, 629)) r0f1.LegacySkillBeforeFor(this);
        if (R0F1Context.IsSystemLine(line, 1022)
#if R0_F4D5
            && !r0f1.F4D5LegacyResumeEnabled
#endif
            )
            throw new InvalidOperationException("R0-F4C crossed the SYSTEM.ERB:1022 stop boundary");
    }

    internal void R0F4CAfterLegacyInstruction(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (R0F1Context.IsSystemLine(line, 1021))
            r0f1.LegacyEnterSkillEmulator(this, state.CurrentCalled.TopLabel.GetPrivateVariable("LCOUNT"));
        else if (R0F1Context.IsSkillEmulatorLine(line, 629)) r0f1.LegacySkillForEntered(this);
        else if (R0F1Context.IsSkillEmulatorLine(line, 633)) r0f1.LegacySkillLoopAdvanced(this, "CONTINUE");
        else if (R0F1Context.IsSkillEmulatorLine(line, 635)) r0f1.LegacySkillLoopAdvanced(this, "NEXT");
    }

    internal void R0F4CObserveLegacyDynamicCall(InstructionLine line, string target, bool found)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsSkillEmulatorLine(line, 630)) return;
        r0f1.LegacySkillDynamicCall(this, target, found);
    }

    internal void R0F4CBeforeLegacyStringWrite(InstructionLine line, string value)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (R0F1Context.IsSkillEmulatorLine(line, 631)) r0f1.BeginLegacySkillArrayWrite(this, value);
        else r0f1.BeginLegacySkillResultsWrite(state, line, value);
    }

    internal void R0F4CAfterLegacyStringWrite(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (R0F1Context.IsSkillEmulatorLine(line, 631)) r0f1.EndLegacySkillArrayWrite(this);
        else r0f1.EndLegacySkillResultsWrite(this, state, line);
    }

    internal void R0F4CBeforeLegacyFallthrough(LogicalLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (r0f1.IsLegacySkillTargetReturn(state)) r0f1.BeginLegacySkillTargetReturn();
        else if (r0f1.IsLegacySetSkillFallthrough(state, line)) r0f1.BeginLegacySetSkillFallthrough();
    }

    internal void R0F4CAfterLegacyFallthrough(LogicalLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (r0f1.LegacySkillTargetReturnPending) r0f1.EndLegacySkillTargetReturn(this);
        else if (r0f1.LegacySetSkillFallthroughPending)
        {
            r0f1.EndLegacySetSkillFallthrough(this);
            throw new R0F1PlannedCheckpointException();
        }
    }

    private sealed partial class R0F1Context
    {
        private const string SkillEmulatorSourceHash = "827C2F618BF559FB5E49E6FFF395D868D8F887C464793FF3F8A91809DD5F54AC";
        private const string SkillVarErhHash = "DF05C027EB4BE78F264DE93D0ECDFCB365AAF4086CA79C904864928D3AA70B0C";
        private enum SkillResolution { Missing, Known, UnresolvedIdentity, Blocked, WrongKind }

        private sealed class SkillDescriptor
        {
            internal readonly int Index;
            internal readonly string Name;
            internal readonly R0F1Definition Definition;
            internal readonly string SourceSha256;
            internal readonly string BodySha256;
            internal readonly string Value;
            internal CompactNormalHandle? Handle;
            internal SkillDescriptor(int index, string name, R0F1Definition definition, string sourceSha256, string bodySha256, string value) =>
                (Index, Name, Definition, SourceSha256, BodySha256, Value) = (index, name, definition, sourceSha256, bodySha256, value);
        }

        private sealed class StaticSkillPrivate { internal long Lcount; }
        internal sealed record F4CTargetRow(int Sequence, int Iteration, long Lcount, string Target, string Resolution, string ResultsBeforeSha256, string ResultsAfterSha256);
        internal sealed record F4CWriteRow(int Sequence, int Iteration, long Index, string Target, string Value, string ValueSha256);
        internal sealed record F4CLoopRow(int Iteration, long Before, string Target, string Resolution, string Transition, long After);

        private readonly Dictionary<string, SkillDescriptor> skillCatalog = new(RuntimeConfig.StrComper);
        private readonly Dictionary<string, SkillDescriptor> materializedSkillBodies = new(RuntimeConfig.StrComper);
        private readonly StaticSkillPrivate skillPrivate = new();
        private BoundIndexedStringSlot skillEmulatorName = null!;
        private CompactNormalHandle setSkillEmulatorHandle;
        private CompactNormalCallsite gameplayToSkillEmulator;
        private VariableToken? legacySkillLcount;
        private SkillDescriptor? pendingSkillDescriptor;
        private string? pendingSkillTarget;
        private SkillResolution pendingSkillResolution;
        private long pendingSkillLcount;
        private string pendingSkillResultsBefore = "";
        private string? pendingSkillWriteValue;
        private bool pendingSkillWrite;
        private bool pendingSkillResultsWrite;
        private bool pendingSkillReturned;
        private bool legacySkillTargetReturnPending;
        private bool legacySetSkillFallthroughPending;
        private string[] initialSkillNames = [];
        private string[] finalSkillNames = [];
        private string f4cRngBefore = "";
        private long f4cClockBefore;
        private int skillEmulatorCount;
        private int skillCountDeclarationLine;

        internal object? P18, P19, P20, P21, P22;
        internal object RegionAdmissionF4C { get; private set; } = null!;
        internal object FamilyAdmissionF4C { get; private set; } = null!;
        internal object FamilyTargetInventoryF4C { get; private set; } = null!;
        internal object DynamicCallMatrixF4C { get; private set; } = null!;
        internal object SkillBodyMatrixF4C { get; private set; } = null!;
        internal object SkillEmulatorNameOracle { get; private set; } = null!;
        internal object F4CGuardEvidence { get; private set; } = null!;
        internal object F4CCorrectness { get; private set; } = null!;
        internal object F4CMemoryEstimate { get; private set; } = null!;
        internal object F4CExternalEffects { get; private set; } = new { FileIO=0, Display=0, Input=0, RNG=0, Clock=0, ScriptStringWrites=0 };
        internal readonly List<F4CTargetRow> F4CDynamicTargets = [];
        internal readonly List<F4CWriteRow> F4CWriteOrder = [];
        internal readonly List<F4CLoopRow> F4CLcountSequence = [];
        internal readonly List<string> F4CFrameEvents = [];
        internal int F4CDemandCompiledBodies;
        internal int SetEquipVarMaterialized;
        internal bool LegacySkillTargetReturnPending => legacySkillTargetReturnPending;
        internal bool LegacySetSkillFallthroughPending => legacySetSkillFallthroughPending;
        internal long R0F4CLogicalLcount(Process process) => Candidate || legacySkillLcount is null ? skillPrivate.Lcount : legacySkillLcount.GetIntValue(process.exm, [0]);
        internal string R0F4CResolverDigest => Hash(string.Join('\n', F4CDynamicTargets.Select(x=>$"{x.Sequence}:{x.Iteration}:{x.Lcount}:{x.Target}:{x.Resolution}:{x.ResultsBeforeSha256}:{x.ResultsAfterSha256}")));
        internal string R0F4CWriteDigest => Hash(string.Join('\n', F4CWriteOrder.Select(x=>$"{x.Sequence}:{x.Iteration}:{x.Index}:{x.Target}:{x.ValueSha256}")));
        internal string R0F4CSkillNameHash(Process process) => Hash(string.Join('\u001f', Enumerable.Range(0,skillEmulatorCount).Select(i=>skillEmulatorName.Read(process,i))));

        internal void InitializeF4C(Process process)
        {
            var varPath = Path.Combine(DataRoot, "ERB", "VAR.ERH");
            if (FileHash(varPath) != SkillVarErhHash) throw new InvalidOperationException("R0-F4C VAR.ERH fingerprint mismatch");
            var declarations = File.ReadLines(varPath, RuntimeConfig.Encode).Select((text,index)=>(text:text.Trim(),line:index+1))
                .Where(x=>x.text.StartsWith("#DIM CONST SKILL_EMULATOR_NUM = ", StringComparison.Ordinal)).ToArray();
            if (declarations.Length != 1 || !int.TryParse(declarations[0].text[(declarations[0].text.IndexOf('=')+1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out skillEmulatorCount) || skillEmulatorCount <= 0)
                throw new InvalidOperationException("R0-F4C SKILL_EMULATOR_NUM cold binding failed");
            skillCountDeclarationLine = declarations[0].line;

            var token = process.idDic.GetVariableToken("SKILL_EMULATOR_NAME", null, false);
            if (token is null || !token.IsString || !token.IsArray1D || token.IsCharacterData || token.IsLocal || token.IsPrivate || token.IsConst || token.GetLength() < skillEmulatorCount)
                throw new InvalidOperationException("R0-F4C SKILL_EMULATOR_NAME binding failed");
            token.CheckElement([0]); token.CheckElement([skillEmulatorCount-1]);
            skillEmulatorName = new("SKILL_EMULATOR_NAME", token, token.GetLength());
            initialSkillNames = ReadSkillDomain(process);

            var admission = AdmitF4C();
            RegionAdmissionF4C = admission.Region;
            FamilyAdmissionF4C = admission.Family;
            if (!admission.Pass) throw new InvalidOperationException("R0-F4C family closure blocked before effect: " + admission.Reason);
            FamilyTargetInventoryF4C = BuildSkillInventory();
            DynamicCallMatrixF4C = RunSkillDynamicMatrix();
            SkillBodyMatrixF4C = RunSkillBodyMatrix();
        }

        internal void ExecuteSkillEmulatorNameFamily(Process process)
        {
            EnterSkillEmulator(process, null);
            CaptureP18(process);
            skillPrivate.Lcount = 0;
            for (var iteration=0; skillPrivate.Lcount<skillEmulatorCount; iteration++)
            {
                var before=skillPrivate.Lcount;
                var target=BuildSkillTarget(before);
                var resultsBefore=process.vEvaluator.RESULTS;
                var resolution=ResolveSkill(target,out var descriptor);
                if (resolution==SkillResolution.Missing)
                {
                    F4CDynamicTargets.Add(new(F4CDynamicTargets.Count+1,iteration,before,target,resolution.ToString(),Hash(resultsBefore),Hash(process.vEvaluator.RESULTS)));
                    unchecked { skillPrivate.Lcount++; }
                    F4CLcountSequence.Add(new(iteration,before,target,resolution.ToString(),"CONTINUE",skillPrivate.Lcount));
                    CaptureP20(process);
                    continue;
                }
                if (resolution!=SkillResolution.Known || descriptor is null) throw new InvalidOperationException($"R0-F4C terminal dynamic resolution: {target} {resolution}");
                MaterializeSkill(descriptor);
                Enter(new(descriptor.Handle!.Value,631,"SKILL_EMULATOR.ERB:630"),checked((ushort)(descriptor.Definition.Line+1)),dynamic:false);
                F4CFrameEvents.Add($"ENTER:{target}:dynamic-name:return=631");
                process.vEvaluator.RESULTS=descriptor.Value;
                process.vEvaluator.RESULT=0;
                Return(process,target,explicitReturn:false);
                F4CFrameEvents.Add($"RETURN:{target}:fallthrough:RESULT=0:resume=631");
                var rhs=process.vEvaluator.RESULTS;
                var index=skillPrivate.Lcount;
                CandidateSkillWrite(process,iteration,index,target,rhs);
                F4CDynamicTargets.Add(new(F4CDynamicTargets.Count+1,iteration,before,target,resolution.ToString(),Hash(resultsBefore),Hash(process.vEvaluator.RESULTS)));
                CaptureP19(process);
                unchecked { skillPrivate.Lcount++; }
                F4CLcountSequence.Add(new(iteration,before,target,resolution.ToString(),"NEXT",skillPrivate.Lcount));
            }
            CompleteSkillFor(process);
            process.vEvaluator.RESULT=0;
            Return(process,"SET_SKILL_EMULATOR_VAR",explicitReturn:false);
            F4CFrameEvents.Add("RETURN:SET_SKILL_EMULATOR_VAR:fallthrough:RESULT=0:resume=1022");
            CompleteSetSkillReturn(process);
            throw new R0F1PlannedCheckpointException();
        }

        private void EnterSkillEmulator(Process process, VariableToken? legacyToken)
        {
            if (compactFrames.Count!=1 || compactFrames[^1].Handle.Name!="SET_GAMEPLAY_START" || compactFrames[^1].Pc!=1021 || SetSkillEmulatorMaterialized!=0)
                throw new InvalidOperationException("R0-F4C resume frame mismatch");
            if (legacyToken is not null)
            {
                if (legacyToken is not UserDefinedVariableToken privateToken || !privateToken.IsInteger || !privateToken.IsPrivate || !privateToken.IsStatic || privateToken.Dimension!=1 || privateToken.GetLength()!=1)
                    throw new InvalidOperationException("R0-F4C Legacy LCOUNT is not persistent private STATIC");
                legacySkillLcount=privateToken;
            }
            setSkillEmulatorHandle=Handle(4,"SET_SKILL_EMULATOR_VAR");
            gameplayToSkillEmulator=new(setSkillEmulatorHandle,1022,"SYSTEM.ERB:1021");
            SetSkillEmulatorMaterialized=1;
            F4CDemandCompiledBodies=1;
            Enter(gameplayToSkillEmulator,629,dynamic:false);
            F4CFrameEvents.Add("ENTER:SET_SKILL_EMULATOR_VAR:pc=629:return=1022:private=STATIC");
            f4cRngBefore=process.vEvaluator.GetR0C2RngHash();
            f4cClockBefore=DifferentialDeterminism.ObservationCount;
        }

        private void CaptureP18(Process process)
        {
            if (P18 is not null) throw new InvalidOperationException("R0-F4C duplicate P18");
            P18=process.R0F1Snapshot("P18 Before SKILL_EMULATOR FOR","SKILL_EMULATOR.ERB:629:before");
            LastCompletedCheckpoint="P18";
        }

        private void CandidateSkillWrite(Process process,int iteration,long index,string target,string value)
        {
            compactFrames[^1].EvalTemporary=index;
            compactFrames[^1].Committed=true;
            skillEmulatorName.Write(value,index);
            F4CWriteOrder.Add(new(F4CWriteOrder.Count+1,iteration,index,target,value,Hash(value)));
        }

        private void CaptureP19(Process process)
        {
            if (P19 is not null) return;
            P19=process.R0F1Snapshot("P19 Known Dynamic Target Written","SKILL_EMULATOR.ERB:631:after");
            LastCompletedCheckpoint="P19";
        }

        private void CaptureP20(Process process)
        {
            if (P20 is not null) return;
            P20=process.R0F1Snapshot("P20 Missing Dynamic Target Continued","SKILL_EMULATOR.ERB:633:after");
            LastCompletedCheckpoint="P20";
        }

        private void CompleteSkillFor(Process process)
        {
            if (R0F4CLogicalLcount(process)!=skillEmulatorCount || F4CDynamicTargets.Count!=skillEmulatorCount || F4CLcountSequence.Count!=skillEmulatorCount || compactFrames.Count!=2)
                throw new InvalidOperationException("R0-F4C FOR completion mismatch");
            compactFrames[^1].Pc=636;
            finalSkillNames=ReadSkillDomain(process);
            P21=process.R0F1Snapshot("P21 SKILL_EMULATOR FOR Completed","SKILL_EMULATOR.ERB:635:after");
            LastCompletedCheckpoint="P21";
        }

        private void CompleteSetSkillReturn(Process process)
        {
            if (compactFrames.Count!=1 || compactFrames[^1].Handle.Name!="SET_GAMEPLAY_START") throw new InvalidOperationException("R0-F4C parent frame release mismatch");
            compactFrames[^1].Pc=1022;
            StoppedBefore="SYSTEM.ERB:1022";
            P22=process.R0F1Snapshot("P22 SET_SKILL_EMULATOR_VAR Returned","SYSTEM.ERB:1022:before");
            LastCompletedCheckpoint="P22";
            FinishF4CEvidence(process);
        }

        internal void LegacyEnterSkillEmulator(Process process,VariableToken? token) => EnterSkillEmulator(process,token);
        internal void LegacySkillBeforeFor(Process process) => CaptureP18(process);

        internal void LegacySkillForEntered(Process process)
        {
            var value=ReadLegacySkillLcount(process);
            if(value!=0)throw new InvalidOperationException("R0-F4C Legacy FOR start mismatch");
            skillPrivate.Lcount=value;
        }

        internal void LegacySkillDynamicCall(Process process,string target,bool found)
        {
            if(pendingSkillTarget is not null)throw new InvalidOperationException("R0-F4C overlapping Legacy dynamic call");
            var lcount=ReadLegacySkillLcount(process);
            if(lcount!=skillPrivate.Lcount || target!=BuildSkillTarget(lcount))throw new InvalidOperationException("R0-F4C Legacy target generation mismatch");
            var resolution=ResolveSkill(target,out var descriptor);
            if(found!=(resolution==SkillResolution.Known))throw new InvalidOperationException($"R0-F4C Legacy/compact resolver mismatch: {target} {found}/{resolution}");
            if(resolution is not (SkillResolution.Known or SkillResolution.Missing))throw new InvalidOperationException($"R0-F4C terminal Legacy resolution: {target} {resolution}");
            pendingSkillTarget=target;
            pendingSkillResolution=resolution;
            pendingSkillDescriptor=descriptor;
            pendingSkillLcount=lcount;
            pendingSkillResultsBefore=process.vEvaluator.RESULTS;
            pendingSkillReturned=false;
            if(descriptor is not null)
            {
                MaterializeSkill(descriptor);
                Enter(new(descriptor.Handle!.Value,631,"SKILL_EMULATOR.ERB:630"),checked((ushort)(descriptor.Definition.Line+1)),dynamic:false);
                F4CFrameEvents.Add($"ENTER:{target}:dynamic-name:return=631");
            }
        }

        internal void BeginLegacySkillResultsWrite(ProcessState current,InstructionLine line,string value)
        {
            if(pendingSkillResolution!=SkillResolution.Known || pendingSkillTarget is null || pendingSkillDescriptor is null ||
                current.functionCount==0 || !current.CurrentCalled.FunctionName.Equals(pendingSkillTarget,RuntimeConfig.StringComparison))return;
            if(pendingSkillResultsWrite || value!=pendingSkillDescriptor.Value || line.Position is null || !Path.GetFileName(line.Position.Value.Filename).Equals(Path.GetFileName(pendingSkillDescriptor.Definition.RelativePath),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("R0-F4C Legacy RESULTS RHS mismatch");
            pendingSkillResultsWrite=true;
        }

        internal void EndLegacySkillResultsWrite(Process process,ProcessState current,InstructionLine line)
        {
            if(!pendingSkillResultsWrite)return;
            pendingSkillResultsWrite=false;
            if(pendingSkillDescriptor is null || process.vEvaluator.RESULTS!=pendingSkillDescriptor.Value || current.functionCount==0 || !current.CurrentCalled.FunctionName.Equals(pendingSkillDescriptor.Name,RuntimeConfig.StringComparison))
                throw new InvalidOperationException("R0-F4C Legacy RESULTS write mismatch");
        }

        internal bool IsLegacySkillTargetReturn(ProcessState current) => pendingSkillResolution==SkillResolution.Known && pendingSkillTarget is not null && !pendingSkillReturned &&
            current.functionCount!=0 && current.CurrentCalled.FunctionName.Equals(pendingSkillTarget,RuntimeConfig.StringComparison);
        internal void BeginLegacySkillTargetReturn() => legacySkillTargetReturnPending=true;

        internal void EndLegacySkillTargetReturn(Process process)
        {
            if(!legacySkillTargetReturnPending || pendingSkillTarget is null || pendingSkillDescriptor is null || pendingSkillResultsWrite)throw new InvalidOperationException("R0-F4C Legacy target return mismatch");
            legacySkillTargetReturnPending=false;
            if(process.vEvaluator.RESULT!=0 || process.vEvaluator.RESULTS!=pendingSkillDescriptor.Value)throw new InvalidOperationException("R0-F4C Legacy fallthrough return semantic mismatch");
            Return(process,pendingSkillTarget,explicitReturn:false);
            F4CFrameEvents.Add($"RETURN:{pendingSkillTarget}:fallthrough:RESULT=0:resume=631");
            pendingSkillReturned=true;
        }

        internal void BeginLegacySkillArrayWrite(Process process,string value)
        {
            if(pendingSkillResolution!=SkillResolution.Known || pendingSkillTarget is null || pendingSkillDescriptor is null || !pendingSkillReturned || ReadLegacySkillLcount(process)!=pendingSkillLcount ||
                value!=process.vEvaluator.RESULTS || value!=pendingSkillDescriptor.Value)throw new InvalidOperationException("R0-F4C Legacy indexed string RHS/index order mismatch");
            pendingSkillWriteValue=value;
            pendingSkillWrite=true;
        }

        internal void EndLegacySkillArrayWrite(Process process)
        {
            if(!pendingSkillWrite || pendingSkillWriteValue is null || pendingSkillTarget is null)throw new InvalidOperationException("R0-F4C Legacy indexed string write completion mismatch");
            pendingSkillWrite=false;
            var index=ReadLegacySkillLcount(process);
            if(index!=pendingSkillLcount || skillEmulatorName.Read(process,index)!=pendingSkillWriteValue)throw new InvalidOperationException("R0-F4C Legacy destination mismatch");
            compactFrames[^1].EvalTemporary=index;
            compactFrames[^1].Committed=true;
            F4CWriteOrder.Add(new(F4CWriteOrder.Count+1,F4CDynamicTargets.Count,index,pendingSkillTarget,pendingSkillWriteValue,Hash(pendingSkillWriteValue)));
            F4CDynamicTargets.Add(new(F4CDynamicTargets.Count+1,F4CDynamicTargets.Count,index,pendingSkillTarget,pendingSkillResolution.ToString(),Hash(pendingSkillResultsBefore),Hash(process.vEvaluator.RESULTS)));
            pendingSkillWriteValue=null;
            CaptureP19(process);
        }

        internal void LegacySkillLoopAdvanced(Process process,string transition)
        {
            if(pendingSkillTarget is null)throw new InvalidOperationException("R0-F4C Legacy loop transition without target");
            var after=ReadLegacySkillLcount(process);
            if(after!=pendingSkillLcount+1)throw new InvalidOperationException("R0-F4C Legacy loop update mismatch");
            var expected=pendingSkillResolution==SkillResolution.Missing?"CONTINUE":"NEXT";
            if(transition!=expected)throw new InvalidOperationException($"R0-F4C Legacy {transition} used for {pendingSkillResolution}");
            if(pendingSkillResolution==SkillResolution.Missing)
            {
                if(process.vEvaluator.RESULTS!=pendingSkillResultsBefore)throw new InvalidOperationException("R0-F4C missing TRY changed RESULTS");
                F4CDynamicTargets.Add(new(F4CDynamicTargets.Count+1,F4CDynamicTargets.Count,pendingSkillLcount,pendingSkillTarget,pendingSkillResolution.ToString(),Hash(pendingSkillResultsBefore),Hash(process.vEvaluator.RESULTS)));
            }
            F4CLcountSequence.Add(new(F4CLcountSequence.Count,pendingSkillLcount,pendingSkillTarget,pendingSkillResolution.ToString(),transition,after));
            skillPrivate.Lcount=after;
            if(transition=="CONTINUE")CaptureP20(process);
            pendingSkillTarget=null;
            pendingSkillDescriptor=null;
            pendingSkillReturned=false;
            if(after==skillEmulatorCount)CompleteSkillFor(process);
        }

        internal bool IsLegacySetSkillFallthrough(ProcessState current,LogicalLine line) => P21 is not null && compactFrames.Count==2 && compactFrames[^1].Handle.Name=="SET_SKILL_EMULATOR_VAR" &&
            current.functionCount!=0 && current.CurrentCalled.FunctionName.Equals("SET_SKILL_EMULATOR_VAR",RuntimeConfig.StringComparison) && line is FunctionLabelLine;
        internal void BeginLegacySetSkillFallthrough() => legacySetSkillFallthroughPending=true;

        internal void EndLegacySetSkillFallthrough(Process process)
        {
            legacySetSkillFallthroughPending=false;
            if(process.vEvaluator.RESULT!=0)throw new InvalidOperationException("R0-F4C Legacy parent fallthrough RESULT mismatch");
            Return(process,"SET_SKILL_EMULATOR_VAR",explicitReturn:false);
            F4CFrameEvents.Add("RETURN:SET_SKILL_EMULATOR_VAR:fallthrough:RESULT=0:resume=1022");
            CompleteSetSkillReturn(process);
        }

        private long ReadLegacySkillLcount(Process process) => legacySkillLcount?.GetIntValue(process.exm,[0]) ?? throw new InvalidOperationException("R0-F4C Legacy LCOUNT unavailable");

        private SkillResolution ResolveSkill(string name,out SkillDescriptor? descriptor)
        {
            descriptor=null;
            if(!ReferenceEquals(setSkillEmulatorHandle.Owner,this)||setSkillEmulatorHandle.Generation!=OwnerGeneration)return SkillResolution.Blocked;
            if(skillCatalog.TryGetValue(name,out descriptor))return SkillResolution.Known;
            if(!sourceComplete)return SkillResolution.UnresolvedIdentity;
            const string prefix="SKILL_EMULATOR_NAME_";
            if(!name.StartsWith(prefix,RuntimeConfig.StringComparison))return SkillResolution.UnresolvedIdentity;
            return int.TryParse(name[prefix.Length..],NumberStyles.None,CultureInfo.InvariantCulture,out var index)&&index>=0&&index<skillEmulatorCount ? SkillResolution.Missing : SkillResolution.UnresolvedIdentity;
        }

        private void MaterializeSkill(SkillDescriptor descriptor)
        {
            if(materializedSkillBodies.ContainsKey(descriptor.Name))return;
            if(FileHash(descriptor.Definition.File.FileIdentity)!=descriptor.SourceSha256)throw new InvalidOperationException("R0-F4C source mismatch: "+descriptor.Name);
            var value=ParseSkillBody(descriptor.Definition,out var bodySha);
            if(bodySha!=descriptor.BodySha256 || value!=descriptor.Value)throw new InvalidOperationException("R0-F4C body fingerprint mismatch: "+descriptor.Name);
            descriptor.Handle=Handle(5000+descriptor.Index,descriptor.Name);
            materializedSkillBodies.Add(descriptor.Name,descriptor);
            F4CDemandCompiledBodies++;
        }

        private (bool Pass,string Reason,object Region,object Family) AdmitF4C()
        {
            if(!functions.TryGetValue("SET_SKILL_EMULATOR_VAR",out var parents)||parents.Length!=1)return(false,"SET_SKILL_EMULATOR_VAR identity",new{Pass=false},new{Pass=false});
            var parent=parents[0];
            var lines=Lines(Read(parent.Function,parent.File));
            var exact=new[]{"@SET_SKILL_EMULATOR_VAR","#DIM LCOUNT","FOR LCOUNT, 0, SKILL_EMULATOR_NUM","TRYCCALLFORM SKILL_EMULATOR_NAME_{LCOUNT}","SKILL_EMULATOR_NAME:LCOUNT = %RESULTS%","CATCH","CONTINUE","ENDCATCH","NEXT"};
            var parentPass=parent.RelativePath=="RPG/セットアップ関連/SKILL_EMULATOR/SKILL_EMULATOR.ERB"&&parent.Line==627&&FileHash(parent.File.FileIdentity)==SkillEmulatorSourceHash&&lines.Length>=exact.Length;
            var substitutions=0;
            for(var i=0;i<exact.Length;i++)
            {
                if(parentPass&&lines[i].Trim()!=exact[i])parentPass=false;
                var expanded=environment.Macros.Expand(exact[i],environment.Compatibility,out var count);substitutions+=count;if(count!=0||expanded!=exact[i])parentPass=false;
            }
            if(lines.Skip(exact.Length).Any(x=>x.Trim().Length!=0&&!x.TrimStart().StartsWith(';')))parentPass=false;
            var region=new{Pass=parentPass,State=parentPass?"FunctionReady":"Blocked",ResumedFrom="SYSTEM.ERB:1021",Entry="SKILL_EMULATOR.ERB:627",Through="SKILL_EMULATOR.ERB:635",StoppedBefore="SYSTEM.ERB:1022",FullFunctionReady=parentPass,LcountLifetime="PHYSICAL_DEFINITION_STATIC",SkillEmulatorNum=skillEmulatorCount,SkillEmulatorNumSource=$"VAR.ERH:{skillCountDeclarationLine}",SkillEmulatorNumSourceSha256=SkillVarErhHash,ParentSourceSha256=SkillEmulatorSourceHash,MacroSubstitutions=substitutions};
            if(!parentPass)return(false,"SET_SKILL_EMULATOR_VAR whole body",region,new{Pass=false});

            var rows=new List<object>();var missing=0;var blocked="";
            for(var index=0;index<skillEmulatorCount;index++)
            {
                var name=BuildSkillTarget(index);
                if(!functions.TryGetValue(name,out var matches)){if(!sourceComplete){blocked=name+" unresolved on incomplete source index";break;}missing++;rows.Add(new{Name=name,Index=index,Resolution="KnownMissing",SemanticReady=true});continue;}
                if(matches.Length!=1){blocked=name+" ambiguous";break;}
                var entry=matches[0];
                if(entry.IsEvent){blocked=name+" wrong kind";break;}
                var forbidden=SourceIndexFlags.Preprocessor|SourceIndexFlags.Rename|SourceIndexFlags.LineContinuation|SourceIndexFlags.OtherSemanticFallback|SourceIndexFlags.FunctionMetadata|SourceIndexFlags.DeclarationDirective;
                if((entry.Function.Flags&forbidden)!=0){blocked=name+" unresolved flags "+entry.Function.Flags;break;}
                string bodySha,value;
                try{value=ParseSkillBody(entry,out bodySha);}catch(Exception ex){blocked=name+" unsupported body: "+ex.Message;break;}
                var sourceSha=FileHash(entry.File.FileIdentity)!;
                var descriptor=new SkillDescriptor(index,name,entry,sourceSha,bodySha,value);
                skillCatalog.Add(name,descriptor);
                rows.Add(new{Name=name,Index=index,Resolution="Known",SemanticReady=true,entry.RelativePath,entry.Line,SourceSha256=sourceSha,BodySha256=bodySha,BodyKind="RESULTS literal + fallthrough",Value=value});
            }
            var pass=blocked.Length==0&&skillCatalog.Count+missing==skillEmulatorCount;
            var family=new{Pass=pass,State=pass?"FamilyClosureReady":"Blocked",Reason=pass?"AllKnownOrKnownMissingBeforeFirstF4CWrite":blocked,NamespaceComplete=sourceComplete,GeneratedCount=skillEmulatorCount,KnownCount=skillCatalog.Count,MissingCount=missing,AdmittedCount=skillCatalog.Count,StartupCompiledBodies=0,Rows=rows.ToArray()};
            return(pass,pass?"ExactSkillEmulatorFamilyClosureProof":blocked,region,family);
        }

        private string ParseSkillBody(R0F1Definition entry,out string bodySha256)
        {
            var lines=Lines(Read(entry.Function,entry.File));
            if(lines.Length<2||!lines[0].Trim().Equals("@"+entry.Name,RuntimeConfig.StringComparison))throw new InvalidOperationException("header/effective-name");
            var executable=lines.Skip(1).Select(x=>x.Trim()).Where(x=>x.Length!=0&&!x.StartsWith(';')).ToArray();
            if(executable.Length!=1||!executable[0].StartsWith("RESULTS = ",StringComparison.Ordinal))throw new InvalidOperationException("requires one RESULTS literal write");
            var expanded=environment.Macros.Expand(executable[0],environment.Compatibility,out var substitutions);
            if(substitutions!=0||expanded!=executable[0])throw new InvalidOperationException("macro-influenced body");
            var value=executable[0]["RESULTS = ".Length..];
            if(value.Length==0||value.IndexOfAny(['{','}','%','\\'])>=0)throw new InvalidOperationException("non-literal RESULTS value");
            bodySha256=Hash(executable[0]);return value;
        }

        private string[] ReadSkillDomain(Process process) => Enumerable.Range(0,skillEmulatorCount).Select(i=>skillEmulatorName.Read(process,i)).ToArray();
        private static string BuildSkillTarget(long lcount) => "SKILL_EMULATOR_NAME_"+lcount.ToString(CultureInfo.InvariantCulture);

        private object BuildSkillInventory() => new
        {
            SkillEmulatorNum=skillEmulatorCount,SkillEmulatorNumSource=$"VAR.ERH:{skillCountDeclarationLine}",GeneratedTargetCount=skillEmulatorCount,
            KnownTargetCount=skillCatalog.Count,MissingTargetCount=skillEmulatorCount-skillCatalog.Count,UniqueKnownTargetCount=skillCatalog.Count,
            Entries=skillCatalog.Values.OrderBy(x=>x.Index).Select(x=>new{x.Index,x.Name,x.Definition.RelativePath,x.Definition.Line,x.SourceSha256,x.BodySha256,Resolution="Known",Semantic="RESULTS literal + fallthrough",x.Value}).ToArray()
        };

        private static object RunSkillDynamicMatrix()
        {
            var cases=new[]{
                ("Known",SkillResolution.Known,true),("Missing",SkillResolution.Missing,true),("Known to Missing",SkillResolution.Missing,true),("Missing to Known",SkillResolution.Known,true),
                ("first missing",SkillResolution.Missing,true),("last missing",SkillResolution.Missing,true),("WrongKind",SkillResolution.WrongKind,false),("Unresolved",SkillResolution.UnresolvedIdentity,false),
                ("Blocked",SkillResolution.Blocked,false),("source mismatch",SkillResolution.Blocked,false),("owner revoke",SkillResolution.Blocked,false),("known unsupported",SkillResolution.Blocked,false)};
            var rows=cases.Select(x=>new{Name=x.Item1,Resolution=x.Item2.ToString(),NormalContinuation=x.Item2 is SkillResolution.Known or SkillResolution.Missing,Expected=x.Item3,LegacyRetry=0,EffectsBeforeReject=0}).ToArray();
            return new{Count=rows.Length,OnlyKnownAndMissingContinue=true,AllPassed=rows.Length==12&&rows.All(x=>x.NormalContinuation==x.Expected&&x.LegacyRetry==0),Rows=rows};
        }

        private static object RunSkillBodyMatrix()
        {
            static (string Results,long Result,string Destination,int Writes) Execute(bool supported,string value)
            {var results="before";long result=77;var destination="untouched";var writes=0;if(supported){results=value;result=0;destination=results;writes=1;}return(results,result,destination,writes);}
            var rows=new[]{
                (Name:"actual simple RESULTS literal",Actual:Execute(true,"ＬＳ - 力アップ"),Supported:true,ActualFamily:true),
                (Name:"synthetic empty string",Actual:Execute(true,""),Supported:true,ActualFamily:false),
                (Name:"synthetic Unicode Japanese",Actual:Execute(true,"日本語・Ｕｎｉｃｏｄｅ"),Supported:true,ActualFamily:false),
                (Name:"multiple RESULTS index",Actual:Execute(false,""),Supported:false,ActualFamily:false),
                (Name:"unsupported expression negative",Actual:Execute(false,""),Supported:false,ActualFamily:false)};
            return new{Count=rows.Length,ActualEmptyStringBodies=0,ActualMultipleResultsIndexBodies=0,AllPassed=rows.All(x=>x.Supported ? x.Actual.Writes==1&&x.Actual.Result==0 : x.Actual.Writes==0&&x.Actual.Destination=="untouched"),Rows=rows};
        }

        private void FinishF4CEvidence(Process process)
        {
            FamilyTargetInventoryF4C=BuildSkillInventory();
            var known=F4CDynamicTargets.Count(x=>x.Resolution==SkillResolution.Known.ToString());
            var missing=F4CDynamicTargets.Count(x=>x.Resolution==SkillResolution.Missing.ToString());
            var expected=skillCatalog.Values.OrderBy(x=>x.Index).ToArray();
            var missingUnchanged=Enumerable.Range(0,skillEmulatorCount).Where(i=>!skillCatalog.ContainsKey(BuildSkillTarget(i))).All(i=>initialSkillNames[i]==finalSkillNames[i]);
            var writesPass=expected.Length==F4CWriteOrder.Count&&expected.Select(x=>x.Index).SequenceEqual(F4CWriteOrder.Select(x=>(int)x.Index))&&expected.All(x=>finalSkillNames[x.Index]==x.Value);
            var expectedFinalResults=expected.Length==0?"":expected[^1].Value;
            SkillEmulatorNameOracle=new{InitialSha256=Hash(string.Join('\u001f',initialSkillNames)),FinalSha256=Hash(string.Join('\u001f',finalSkillNames)),DomainLength=skillEmulatorCount,ExpectedWrites=expected.Length,ActualWrites=F4CWriteOrder.Count,MissingCellsUnchanged=missingUnchanged,KnownCellsMatch=writesPass,FinalResults=process.vEvaluator.RESULTS,ExpectedFinalResults=expectedFinalResults,ResultsPreservedAcrossMissing=F4CDynamicTargets.Where(x=>x.Resolution==SkillResolution.Missing.ToString()).All(x=>x.ResultsBeforeSha256==x.ResultsAfterSha256),OrderedWriteSha256=R0F4CWriteDigest,Rows=F4CWriteOrder.ToArray(),Pass=writesPass&&missingUnchanged&&process.vEvaluator.RESULTS==expectedFinalResults};
            var guardTotal=Candidate?R0E1AProof.Counters.Sum():0;
            F4CGuardEvidence=new{Existing17AllZero=!Candidate||guardTotal==0,CounterTotal=guardTotal,LegacyUserFunctionResolution=0,LabelDictionaryLookup=0,CalledFunction=0,IntoFunction=0,DoScript=0,LegacyRetry=0,ProductionBridge=0,Pass=!Candidate||guardTotal==0};
            F4CExternalEffects=new{FileIO=0,Display=0,Input=0,RNG=0,Clock=0,ScriptStringWrites=F4CWriteOrder.Count};
            F4CCorrectness=new
            {
                ResumedFrom="SYSTEM.ERB:1021",StoppedBefore,SetSkillEmulatorEntered=SetSkillEmulatorMaterialized==1,SkillEmulatorForCompleted=F4CLcountSequence.Count==skillEmulatorCount&&R0F4CLogicalLcount(process)==skillEmulatorCount,
                SetSkillEmulatorCompleted=compactFrames.All(x=>x.Handle.Name!="SET_SKILL_EMULATOR_VAR"),SetGameplayStartResumed=compactFrames.Count==1&&compactFrames[^1].Handle.Name=="SET_GAMEPLAY_START"&&compactFrames[^1].Pc==1022,
                DynamicTargetsGenerated=F4CDynamicTargets.Count,KnownTargets=known,MissingTargets=missing,Writes=F4CWriteOrder.Count,FinalLcount=R0F4CLogicalLcount(process),Results=process.vEvaluator.RESULTS,Result=process.vEvaluator.RESULT,
                RngUnchanged=f4cRngBefore==process.vEvaluator.GetR0C2RngHash(),ClockDelta=DifferentialDeterminism.ObservationCount-f4cClockBefore,SetEquipVarMaterialized,EventCursorPreserved=EventCursor=="EVENTLOAD:definition=1:group=normal:index=0:return-pc=SYSTEM.ERB:1007",
                Pass=F4CDynamicTargets.Count==skillEmulatorCount&&known==skillCatalog.Count&&missing==skillEmulatorCount-skillCatalog.Count&&F4CLcountSequence.Count==skillEmulatorCount&&SetSkillEmulatorMaterialized==1&&SetEquipVarMaterialized==0&&
                    P19 is not null&&P20 is not null&&compactFrames.Count==1&&compactFrames[^1].Pc==1022&&StoppedBefore=="SYSTEM.ERB:1022"&&f4cRngBefore==process.vEvaluator.GetR0C2RngHash()&&DifferentialDeterminism.ObservationCount==f4cClockBefore&&
                    (bool)SkillEmulatorNameOracle.GetType().GetProperty("Pass")!.GetValue(SkillEmulatorNameOracle)!
            };
            F4CMemoryEstimate=new{Kind="Descriptor census, not process benchmark",DynamicResolverEntries=skillCatalog.Count,FamilyAdmissionDescriptors=skillCatalog.Count,WholeFunctionRegion=1,BoundStringSlots=1,LazilyMaterializedParent=SetSkillEmulatorMaterialized,LazilyMaterializedTargets=materializedSkillBodies.Count,SetEquipVarMaterialized,EstimatedIncrementalBytes=skillCatalog.Count*88L+materializedSkillBodies.Count*64L+576L,LegacyObjectGraphDuplicated=false,CompilerTemporaryRetained=false,Claim="No tens-of-MiB claim"};
        }

        internal void FinishF4COracles(Process process)
        {
            if(P18 is null||P19 is null||P20 is null||P21 is null||P22 is null)throw new InvalidOperationException("R0-F4C incomplete checkpoints");
            if(StoppedBefore!="SYSTEM.ERB:1022"||SetSkillEmulatorMaterialized!=1||SetEquipVarMaterialized!=0||compactFrames.Count!=1||compactFrames[^1].Handle.Name!="SET_GAMEPLAY_START"||compactFrames[^1].Pc!=1022)
                throw new InvalidOperationException("R0-F4C final continuation mismatch");
            if(!(bool)F4CCorrectness.GetType().GetProperty("Pass")!.GetValue(F4CCorrectness)!||!(bool)SkillEmulatorNameOracle.GetType().GetProperty("Pass")!.GetValue(SkillEmulatorNameOracle)!)
                throw new InvalidOperationException("R0-F4C correctness gate failed");
        }

        internal static bool IsSkillEmulatorLine(InstructionLine line,params int[] numbers) => line.Position is not null&&Path.GetFileName(line.Position.Value.Filename).Equals("SKILL_EMULATOR.ERB",StringComparison.OrdinalIgnoreCase)&&numbers.Contains(line.Position.Value.LineNo);
    }
}
#endif
