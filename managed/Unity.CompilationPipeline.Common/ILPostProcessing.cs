using System.Collections.Generic;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Unity.CompilationPipeline.Common
{
    public enum CodeOptimization
    {
        None = 0,
        Debug = 1,
        Release = 2,
    }

    public class AssemblyInfo
    {
        public string Name { get; set; }
        public string OutputDirectory { get; set; }
        public string[] References { get; set; } = new string[0];
        public string[] Defines { get; set; } = new string[0];
        public string[] Files { get; set; } = new string[0];
        public bool AllowUnsafeCode { get; set; }
        public CodeOptimization CodeOptimization { get; set; } = CodeOptimization.Debug;
    }
}

namespace Unity.CompilationPipeline.Common.ILPostProcessing
{
    /// <summary>An assembly's image and symbols, as an IL post-processor reads and returns them.</summary>
    public class InMemoryAssembly
    {
        public byte[] PeData { get; set; }
        public byte[] PdbData { get; set; }

        public InMemoryAssembly(byte[] peData, byte[] pdbData)
        {
            PeData = peData;
            PdbData = pdbData;
        }
    }

    /// <summary>The compiled assembly an IL post-processor is asked about.</summary>
    public interface ICompiledAssembly
    {
        InMemoryAssembly InMemoryAssembly { get; }
        string Name { get; }
        /// <summary>Paths of the assemblies it references.</summary>
        string[] References { get; }
        string[] Defines { get; }
    }

    public class ILPostProcessResult
    {
        public InMemoryAssembly InMemoryAssembly { get; set; }
        public List<DiagnosticMessage> Diagnostics { get; set; }

        public ILPostProcessResult(InMemoryAssembly inMemoryAssembly)
        {
            InMemoryAssembly = inMemoryAssembly;
            Diagnostics = new List<DiagnosticMessage>();
        }

        public ILPostProcessResult(InMemoryAssembly inMemoryAssembly, List<DiagnosticMessage> diagnostics)
        {
            InMemoryAssembly = inMemoryAssembly;
            Diagnostics = diagnostics;
        }
    }

    /// <summary>A transformation of compiled assemblies, which the Editor runs after each compile.</summary>
    public abstract class ILPostProcessor
    {
        public abstract ILPostProcessor GetInstance();
        public abstract bool WillProcess(ICompiledAssembly compiledAssembly);
        public abstract ILPostProcessResult Process(ICompiledAssembly compiledAssembly);
    }
}

namespace Unity.CompilationPipeline.Common.Diagnostics
{
    public class DiagnosticMessage
    {
        public string File { get; set; }
        public DiagnosticType DiagnosticType { get; set; }
        public string MessageData { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public enum DiagnosticType
    {
        Error = 1,
        Warning = 2,
    }
}
