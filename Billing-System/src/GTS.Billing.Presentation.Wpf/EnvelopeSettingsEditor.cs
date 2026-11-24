using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

internal static class EnvelopeSettingsEditor
{
    internal static void Show(Window owner,IReadOnlyList<SettingEntry> entries,
        Func<IReadOnlyList<SettingEdit>,Task<string?>> save,Func<EnvelopeMedia,Task<string?>> calibrate)
    {
        var entry=entries.Single(x=>x.Key==EnvelopeConfiguration.Media.Key);
        var media=EnvelopeConfiguration.Media.Parse(entry.EffectiveText).Value;
        var dialog=new MeasureEditorDialog("Envelope printing settings",false){Owner=owner,Width=790};
        var geometry=new WrapPanel();
        var numbers=new Dictionary<string,TextBox>();
        void Number(string key,string caption,double value)
        {
            var box=dialog.Text(caption,value.ToString("G",CultureInfo.CurrentCulture),16,panel:geometry);
            box.Width=110;
            numbers.Add(key,box);
        }
        Number("width","Paper width (mm)",media.Width);Number("height","Paper height (mm)",media.Height);
        Number("x","Left position (mm)",media.X);Number("y","Top position (mm)",media.Y);
        Number("textWidth","Text width (mm)",media.TextWidth);Number("textHeight","Text height (mm)",media.TextHeight);
        Number("margin","Minimum margin (mm)",media.Margin);Number("size","Font size (pt)",media.Size);
        Number("gap","Line spacing (mm)",media.LineGap);Number("copies","Copies",media.Copies);
        var font=dialog.Text("Font family",media.Font,80,panel:geometry);
        var printer=dialog.Text("Preferred Windows printer",media.Printer,150,panel:geometry);
        var alignment=dialog.Choice("Alignment",new[]{"Left","Center","Right"},media.Alignment,panel:geometry);
        var orientation=dialog.Choice("Orientation",new[]{"Portrait","Landscape"},media.Orientation,panel:geometry);
        dialog.AddCard("Paper, position and typography",geometry);
        var adjustment=new WrapPanel();
        foreach(var (caption,axis,step) in new[]{("Left 1 mm","x",-1),("Right 1 mm","x",1),("Up 1 mm","y",-1),("Down 1 mm","y",1)})
        {
            var button=new Button {Content=caption,Margin=new Thickness(0,0,6,0)};
            adjustment.Children.Add(button);
            button.Click+=(_,_) =>
            {
                var field=numbers[axis];
                if(double.TryParse(field.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var offset) && double.IsFinite(offset))
                    field.Text=Math.Max(0,offset+step).ToString("G",CultureInfo.CurrentCulture);
            };
        }
        dialog.AddCard("Fine alignment",adjustment);
        var fieldReaders=new List<Func<SettingEdit>>();
        foreach(var definition in EnvelopeConfiguration.Fields)
        {
            var ruleEntry=entries.Single(x=>x.Key==definition.Key);
            var value=definition.Parse(ruleEntry.EffectiveText).Value;
            var panel=new WrapPanel();
            var label=dialog.Text("Label",value.Label,60,panel:panel);
            var order=dialog.Text("Order",value.Order.ToString(CultureInfo.CurrentCulture),4,panel:panel);
            var prefix=dialog.Text("Printed prefix",value.Prefix,32,panel:panel);
            var fallback=dialog.Text("Default value",value.Default,200,panel:panel);
            CheckBox Flag(string text,bool enabled)
            {
                var check=new CheckBox {Content=text,IsChecked=enabled,Margin=new Thickness(6,4,12,4)};
                check.Click+=(_,_)=>dialog.MarkChanged();
                panel.Children.Add(check);
                return check;
            }
            var visible=Flag("Visible",value.Visible);var editable=Flag("Editable",value.Editable);
            var required=Flag("Required",value.Required);var bold=Flag("Bold",value.Bold);
            dialog.AddCard(definition.Label,panel);
            fieldReaders.Add(()=>new(definition.Key,definition.Format(new(label.Text,
                int.TryParse(order.Text,out var rank)?rank:-1,visible.IsChecked==true,editable.IsChecked==true,
                required.IsChecked==true,bold.IsChecked==true,prefix.Text,fallback.Text)),ruleEntry.Version));
        }
        EnvelopeMedia? ReadMedia()
        {
            var values=new Dictionary<string,double>();
            foreach(var (key,box) in numbers)
            {
                if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value) || !double.IsFinite(value))
                    return null;
                values.Add(key,value);
            }
            if(values["copies"] is <1 or >5 || values["copies"]!=Math.Truncate(values["copies"])) return null;
            var proposal=new EnvelopeMedia(values["width"],values["height"],values["x"],values["y"],values["textWidth"],
                values["textHeight"],values["margin"],font.Text.Trim(),values["size"],values["gap"],
                alignment.SelectedItem as string??"",printer.Text.Trim(),(int)values["copies"],orientation.SelectedItem as string??"");
            return EnvelopeConfiguration.Media.TryNormalize(EnvelopeConfiguration.Media.Format(proposal),out _)?proposal:null;
        }
        var calibration=dialog.FooterButton("Print alignment guide");
        calibration.Click+=async (_,_) =>
        {
            if(ReadMedia() is not { } proposal || !EnvelopeConfiguration.Calibrated(proposal))
            {dialog.ShowMessage("Enter the measured paper and block dimensions first.");return;}
            if(MessageBox.Show(dialog,"Use plain paper or spare envelope stock for the alignment guide. Continue?",
                "Print alignment guide",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
            calibration.IsEnabled=false;
            try {dialog.ShowMessage(await calibrate(proposal)??"");}
            finally {calibration.IsEnabled=true;}
        };
        dialog.Save(async ()=>
        {
            if(ReadMedia() is not { } proposal) return EnvelopeConfiguration.Media.Description;
            var changes=new List<SettingEdit> {new(entry.Key,EnvelopeConfiguration.Media.Format(proposal),entry.Version)};
            changes.AddRange(fieldReaders.Select(read=>read()));
            return await save(changes);
        },"Save settings",primary:true);
        dialog.ShowDialog();
    }
}
