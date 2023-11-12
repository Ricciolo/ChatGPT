using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatGPT.Ingestion;
internal record SourceItem(string Id, bool Public, Uri Uri, string Title, string Text);
