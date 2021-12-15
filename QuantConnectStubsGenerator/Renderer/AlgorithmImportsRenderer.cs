using System.IO;

namespace QuantConnectStubsGenerator.Renderer
{
    public class AlgorithmImportsRenderer : BaseRenderer
    {
        private readonly string _leanPath;

        public AlgorithmImportsRenderer(StreamWriter writer, string leanPath) : base(writer)
        {
            _leanPath = leanPath;
        }

        public void Render()
        {
            // var algorithmImports = Path.GetFullPath("Common/AlgorithmImports.py", _leanPath);
            var algorithmImports = Path.Combine(_leanPath, "Common/AlgorithmImports.py");
            WriteLine(File.ReadAllText(algorithmImports));
        }
    }
}
