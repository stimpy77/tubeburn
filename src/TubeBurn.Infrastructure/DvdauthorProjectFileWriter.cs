using System.Xml.Linq;
using TubeBurn.Domain;

namespace TubeBurn.Infrastructure;

public sealed class DvdauthorProjectFileWriter
{
    public string Write(TubeBurnProject project, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        Directory.CreateDirectory(workingDirectory);

        var outputDirectory = Path.Combine(workingDirectory, "VIDEO_TS");
        Directory.CreateDirectory(outputDirectory);

        var xmlPath = Path.Combine(workingDirectory, "project.xml");

        var titles = new XElement("titles",
            new XElement(
                "video",
                new XAttribute("format", project.Settings.Standard == VideoStandard.Ntsc ? "ntsc" : "pal"),
                new XAttribute("aspect", "16:9")),
            new XElement(
                "audio",
                new XAttribute("format", "ac3"),
                new XAttribute("lang", "en")));

        foreach (var video in project.Videos)
        {
            titles.Add(
                new XElement(
                    "pgc",
                    new XElement("vob", new XAttribute("file", video.TranscodedPath)),
                    new XElement("post", "exit;")));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "dvdauthor",
                new XAttribute("dest", outputDirectory),
                new XElement("titleset", titles)));

        document.Save(xmlPath);
        return xmlPath;
    }
}
