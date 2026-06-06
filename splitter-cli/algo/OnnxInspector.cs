using System.Text;
using Onnxify;

public static class OnnxInspector
{
    public static string GetOnnxInfo(string modelPath)
    {
        var sb = new StringBuilder(4096);

        if (!File.Exists(modelPath))
        {
            sb.Append("File not found: ").Append(modelPath);
            return sb.ToString();
        }

        // Load ONNX model
        var model = OnnxModel.FromFile(modelPath);

        sb.AppendLine("=== MODEL METADATA ===");
        sb.Append("IR Version: ").AppendLine(model.IrVersion.ToString());
        sb.Append("Producer Name: ").AppendLine(model.ProducerName);
        sb.Append("Producer Version: ").AppendLine(model.ProducerVersion);
        sb.Append("Domain: ").AppendLine(model.Domain);
        sb.Append("Model Version: ").AppendLine(model.ModelVersion.ToString());
        sb.Append("Doc String: ").AppendLine(model.Document);
        sb.AppendLine();

        sb.AppendLine("=== OPSET IMPORTS ===");
        foreach (var opset in model.OpsetImport)
        {
            sb.Append("Domain: ").Append(opset.Domain)
              .Append("  Version: ").AppendLine(opset.Version.ToString());
        }
        sb.AppendLine();

        var graph = model.Graph;

        sb.AppendLine("=== GRAPH INPUTS ===");
        foreach (var input in graph.Inputs)
        {
            sb.Append("Name: ").AppendLine(input.Name);
            if (input.Type?.Denotation != null)
            {
                sb.Append("  Denotation: ").AppendLine(input.Type?.Denotation);
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== GRAPH OUTPUTS ===");
        foreach (var output in graph.Outputs)
        {
            sb.Append("Name: ").AppendLine(output.Name);
            if (output.Type?.Denotation != null)
            {
                sb.Append("  Denotation: ").AppendLine(output.Type?.Denotation);
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== INITIALIZERS ===");
        foreach (var init in graph.Initializers)
        {
            sb.Append("Name: ").AppendLine(init.Name);
            sb.Append("  DataType: ").AppendLine(init.DataType.ToString());
            sb.Append("  Dims: ").AppendLine(string.Join("x", init.Shape));
        }
        sb.AppendLine();

        sb.AppendLine("=== NODES ===");
        foreach (var node in graph.Nodes)
        {
            sb.Append("OpType: ").AppendLine(node.OpType);
            sb.Append("  Name: ").AppendLine(node.Name);
            sb.Append("  Inputs: ").AppendLine(string.Join(", ", node.Inputs));
            sb.Append("  Outputs: ").AppendLine(string.Join(", ", node.Outputs));

            foreach (var attr in node.Attributes)
            {
                sb.Append("    Attr: ").Append(attr.Name);
            }
        }

        return sb.ToString();
    }
}
