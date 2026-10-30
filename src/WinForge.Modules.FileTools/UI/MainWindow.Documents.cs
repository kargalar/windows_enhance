using System.Windows;
using WinForge.FileTools.Engine;
namespace WinForge.FileTools.UI;
public sealed partial class MainWindow
{
    private readonly OfficeRuntime officeRuntime=new(Path.Combine(WinForge.Core.AppData.DirectoryPath,"Nashi","Tools"));
    private readonly MetadataService metadataService=new(Path.Combine(WinForge.Core.AppData.DirectoryPath,"Nashi","Tools"));
    private async Task EditMetadataAsync(string[] selected)
    {
        if(selected.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)&&FeatureCatalog.Extension(p)!="gif")){await EditMediaMetadataAsync(selected);return;}
        if(metadataService.Executable is null)
        {
            if(!await ConfirmAsync("Set up metadata tools","Download ExifTool (~11 MB) from its official SourceForge distribution? The SHA-256 is checked and it runs locally without an administrator password. Upstream licenses are retained.","Download ExifTool","Cancel"))return;
            SetBusy(true);using var token=new CancellationTokenSource();var toast=new ToastWindow(this,token.Cancel,_featureUi);toast.Show();try{await metadataService.InstallAsync(new Progress<OperationProgress>(toast.Update),token.Token);}finally{toast.Close();SetBusy(false);}
        }
        var removeAll=selected.Length>1;Dictionary<string,string>? changes=null;
        if(selected.Length==1){var values=await metadataService.ReadAsync(selected[0]);var editor=new MetadataEditor(this,values,featureUi:_featureUi);if(editor.ShowDialog()!=true)return;removeAll=editor.RemoveAll;changes=removeAll?null:editor.Updates;}
        await RunAsync((destination,p,t)=>metadataService.WriteAsync(selected,destination,removeAll,changes,progress:p,token:t),"Metadata");
    }
    internal static string ToolSymbol(string id)=>id switch{"compress"=>"↘","removeMetadata"=>"◇","editImage"=>"☷","annotateImage"=>"✎","frameImage"=>"▣","cropImage" or "cropVideo"=>"⌗","trimAudio" or "trimVideo"=>"✂","muteVideo"=>"♫","normalizeAudio"=>"≋","audioChannels"=>"⇆","audioToVideo"=>"▷","createCollage"=>"▦","redactImage" or "redactVideo" or "redactAudio"=>"▨","readQRCode"=>"▧","mergePDF"=>"▣","splitPDF" or "splitVideo"=>"◫","organizePDF"=>"▤","extractArchive"=>"↗","createPDF"=>"▱","joinVideos"=>"⇥","videoSnapshots"=>"▣","changeVideoSpeed"=>"◴",_=>"↗"};
    private async Task SetupOfficeAsync()
    {
        if(busy)return;if(officeRuntime.Executable is not null){L10n.SetText(status,"Office tools are ready.");return;}
        if(!await ConfirmAsync("Set up office tools","Download LibreOffice 26.8.1 (~360 MB) from The Document Foundation? Its SHA-256 is checked, then the package is extracted into Nashi’s user tools folder. No administrator password or Office subscription is needed. LibreOffice retains its upstream licenses.","Download LibreOffice","Cancel"))return;
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new ToastWindow(this,cancellation.Cancel,_featureUi);toast.Show();try{await officeRuntime.InstallAsync(new Progress<OperationProgress>(toast.Update),cancellation.Token);toast.Complete([]);L10n.SetText(status,"Office tools ready.");}catch(OperationCanceledException){toast.Close();L10n.SetText(status,"Setup cancelled.");}catch(Exception error){toast.Close();await ShowMessageAsync("Office setup could not finish",error.Message);}finally{SetBusy(false);}
    }
    private async Task EditDocumentAsync(ToolChoice choice,string[] selected,string? requestedFormat=null)
    {
        if(choice.Id=="convert")
        {
            var editor=new ToolEditor(this,"convert","Convert",selected[0],inputs:selected,outputFormats:FeatureCatalog.Formats(selected),featureUi:_featureUi);editor.ConfigureProfiles(preferences,requestedFormat);if(editor.ShowDialog()!=true)return;
            await RunAsync((destination,p,t)=>new DocumentConversion(officeRuntime,tools).ConvertAsync(selected,editor.Format,destination,editor.DocumentOptions,p,t),"Convert to "+editor.Format.ToUpperInvariant());return;
        }
        if(choice.Id=="organizePDF")
        {
            var organizer=await PdfOrganizer.CreateAsync(this,selected[0],_featureUi);if(organizer.ShowDialog()!=true)return;
            // Read the organizer's WPF state on the UI thread; the export runs on a worker thread.
            var pages=organizer.Pages;var rotations=organizer.Rotations;var pageRotations=organizer.PageRotations;var uniformWidth=organizer.UniformWidth;
            await RunAsync((destination,p,t)=>Task.Run(()=>DocumentEngine.Pdf("organizePDF",selected,destination,pages,t,rotations,pageRotations:pageRotations,uniformWidth:uniformWidth),t),"Organize PDF");return;
        }
        var dialog=new ToolEditor(this,choice.Id,choice.Title,selected[0],inputs:selected,featureUi:_featureUi);if(dialog.ShowDialog()!=true)return;if(choice.Id=="compress"){await RunAsync((destination,p,t)=>PdfCompression.RunAsync(selected,destination,dialog.ImageOptions,dialog.DocumentOptions.Password,p,t),"Compress PDF");return;}
        // PagesPerFile and PageRanges read editor text boxes, so capture them before leaving the UI thread.
        var inputs=dialog.OrderedInputs;var pagesPerFile=dialog.PagesPerFile;var password=dialog.DocumentOptions.Password;var pageRanges=dialog.PageRanges;
        await RunAsync((destination,p,t)=>Task.Run(()=>DocumentEngine.Pdf(choice.Id,inputs,destination,null,t,pagesPerFile:pagesPerFile,password:password,pageRanges:pageRanges),t),choice.Title);
    }
    private async Task ReadQrAsync(IReadOnlyList<string> inputs)
    {
        SetBusy(true);var results=new List<(string File,string Value)>();try
        {
            foreach(var file in inputs)
            {
                if(FeatureCatalog.Extension(file)=="pdf"){var pages=await NativePdf.RenderAsync(file,180);for(var n=0;n<pages.Length;n++)foreach(var value in NativePdf.DecodeQr(ImageEngine.FromPng(pages[n])))results.Add((Path.GetFileName(file)+$" · Page {n+1}",value));}
                else foreach(var value in NativePdf.DecodeQr(ImageEngine.Load(file)))results.Add((Path.GetFileName(file),value));
            }
            var window = new QrResultsWindow(this, results, _featureUi);
            window.ShowDialog();
        }finally{SetBusy(false);}
    }
}
