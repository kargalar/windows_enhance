using System.Windows;
using System.Windows.Controls;
using WinForge.FileTools.Engine;
namespace WinForge.FileTools.UI;
public sealed partial class ToolEditor
{
    private readonly CancellationTokenSource previewLifetime=new();private Toolchain? previewTools;private string? previewDirectory,originalPreviewPath;private bool triedProxy;
    internal void ConfigureMediaRuntime(Toolchain tools)=>previewTools=tools;
    private string PreviewDirectory()=>previewDirectory??=Directory.CreateDirectory(Path.Combine(WinForge.Core.AppData.TemporaryDirectory,"Nashi-preview-"+Guid.NewGuid().ToString("N"))).FullName;
    internal async Task CreatePreviewProxy()
    {
        if(processedPlayback||processingPreview){SetEditorStatus("Processed preview unavailable for this Windows codec.");return;}
        if(triedProxy||previewTools?.Ready!=true){SetEditorStatus("This Windows codec cannot play directly. Set up media tools to create a local preview.");return;}triedProxy=true;time.Text="Preparing a local preview…";PushOptionsState();
        try
        {
            var output=Path.Combine(PreviewDirectory(),media?.Video is null?"preview.wav":"preview.mp4");var args=new List<string>{"-v","error","-nostdin","-i",input};if(media?.Video is not null)args.AddRange(["-vf","scale=w='min(1280,iw)':h=-2","-c:v","libx264","-preset","veryfast","-crf","25","-pix_fmt","yuv420p","-c:a","aac","-movflags","+faststart"]);else args.AddRange(["-vn","-c:a","pcm_s16le"]);args.Add(output);
            await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),args,previewLifetime.Token);if(previewLifetime.IsCancellationRequested)return;originalPreviewPath=output;if(processedPlayback||processingPreview)return;player.Source=new Uri(output);player.Play();player.Pause();playing=false;Seek(0);time.Text="Preview ready · export uses your original file.";PushOptionsState();
        }catch(OperationCanceledException){}catch(Exception error){if(IsVisible)SetEditorStatus("Preview unavailable: "+error.Message);}
    }
    private void DeletePreviewFiles(){if(previewDirectory is null)return;var path=previewDirectory;_ = Task.Run(async()=>{for(var n=0;n<10;n++){try{if(Directory.Exists(path))Directory.Delete(path,true);return;}catch(IOException){await Task.Delay(200);}catch(UnauthorizedAccessException){return;}}});}
}
