using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LlamaSwapManager.Models;

/// <summary>
/// Represents a model item being edited in the UI.
/// </summary>
public partial class ModelEditItem : ObservableObject
{
    [ObservableProperty] private string _modelId = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _llamaServerPath = "";
    [ObservableProperty] private bool _useJinja;
    [ObservableProperty] private bool _fitOn;
    [ObservableProperty] private bool _noMmap;
    [ObservableProperty] private string _ttl = "0";
    [ObservableProperty] private string _aliasesText = "";
    [ObservableProperty] private bool _isNew = false;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _extraArgs = "";
    [ObservableProperty] private string _reasoning = "";
    [ObservableProperty] private string _reasoningFormat = "";
    [ObservableProperty] private string _gpuLayers = "";
    [ObservableProperty] private string _splitMode = "";
    [ObservableProperty] private bool _flashAttention;
    [ObservableProperty] private string _threads = "";
    [ObservableProperty] private string _threadsBatch = "";
    [ObservableProperty] private string _batchSize = "";
    [ObservableProperty] private string _uBatchSize = "";
    [ObservableProperty] private bool _parallel;
    [ObservableProperty] private string _timeout = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _embeddings;
    [ObservableProperty] private bool _reranking;
    [ObservableProperty] private bool _metrics;
    [ObservableProperty] private string _propsEndpoint = "";
    [ObservableProperty] private bool _mlock;
    
    // Missing properties for MainViewModel
    [ObservableProperty] private string _modelPath = "";
    [ObservableProperty] private string _hfModel = "";
    [ObservableProperty] private string _selectedQuantization = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _slots;
    [ObservableProperty] private string _buildCmd = "";

    public ModelEditItem() { }

    public ModelEditItem(string modelId, string name, string llamaServerPath)
    {
        ModelId = modelId;
        Name = name;
        LlamaServerPath = llamaServerPath;
    }

    public static ModelEditItem Parse(string key, string value)
    {
        return new ModelEditItem
        {
            ModelId = key,
            Name = value,
            LlamaServerPath = ""
        };
    }

    public ModelEditItem CloneAs(string newModelId)
    {
        return new ModelEditItem(newModelId, Name, LlamaServerPath)
        {
            UseJinja = UseJinja,
            FitOn = FitOn,
            NoMmap = NoMmap,
            Ttl = Ttl,
            AliasesText = AliasesText,
            IsNew = IsNew,
            ExtraArgs = ExtraArgs,
            Reasoning = Reasoning,
            ReasoningFormat = ReasoningFormat,
            GpuLayers = GpuLayers,
            SplitMode = SplitMode,
            FlashAttention = FlashAttention,
            Threads = Threads,
            ThreadsBatch = ThreadsBatch,
            BatchSize = BatchSize,
            UBatchSize = UBatchSize,
            Parallel = Parallel,
            Timeout = Timeout,
            ApiKey = ApiKey,
            Embeddings = Embeddings,
            Reranking = Reranking,
            Metrics = Metrics,
            PropsEndpoint = PropsEndpoint,
            Mlock = Mlock,
            ModelPath = ModelPath,
            HfModel = HfModel,
            SelectedQuantization = SelectedQuantization,
            Description = Description,
            Slots = Slots,
            BuildCmd = BuildCmd
        };
    }
}
