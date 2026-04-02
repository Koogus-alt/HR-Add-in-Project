using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Heerim_FamilyBrowser
{
    public class LibraryFolder
    {
        public string Name { get; set; } = string.Empty;
        public List<LibraryFolder> SubFolders { get; set; } = new List<LibraryFolder>();
        public bool IsCategory => Name.Length > 3 && char.IsDigit(Name[0]) && char.IsDigit(Name[1]) && char.IsDigit(Name[2]) && Name[3] == '_';
        
        public string CleanName => IsCategory ? Name.Substring(4) : Name;
    }

    public static class LibraryTreeParser
    {
        public static LibraryFolder Parse(string xmlPath)
        {
            if (!System.IO.File.Exists(xmlPath))
                throw new System.IO.FileNotFoundException("tree.xml 파일을 찾을 수 없습니다.", xmlPath);

            XDocument doc = XDocument.Load(xmlPath);
            XElement root = doc.Root;

            if (root == null) throw new Exception("tree.xml 파일의 루트가 없습니다.");

            return ParseFolder(root);
        }

        private static LibraryFolder ParseFolder(XElement element)
        {
            var folder = new LibraryFolder
            {
                Name = element.Attribute("name")?.Value ?? "Unknown"
            };

            foreach (var subElement in element.Elements("folder"))
            {
                folder.SubFolders.Add(ParseFolder(subElement));
            }

            return folder;
        }
    }
}
