// Renders protoc-gen-doc's JSON. Text from the contract only ever goes in through text nodes, never as HTML,
// so a comment can't add markup to the page.
(() => {
  const { files } = JSON.parse(contract);

  const el = (tag, attributes, ...children) => {
    const node = document.createElement(tag);
    for (const [name, value] of Object.entries(attributes ?? {})) {
      if (value !== undefined && value !== null) node.setAttribute(name, value);
    }
    for (const child of children.flat()) {
      if (child !== undefined && child !== null && child !== false) node.append(child);
    }
    return node;
  };

  const deprecated = item => item.options?.deprecated === true;
  const badge = (kind, text) => el("span", { class: `badge ${kind}` }, text);

  // A comment's blank lines split paragraphs; its other line breaks are only where the author wrapped.
  const paragraphs = text => (text ?? "").trim().split(/\n\s*\n/).filter(Boolean).map(p => el("p", {}, p.replace(/\s*\n\s*/g, " ")));

  // Every message and enum by full name, so a field's type can link to it.
  const types = new Map();
  for (const file of files) {
    for (const message of file.messages) types.set(message.fullName, message);
    for (const e of file.enums) types.set(e.fullName, e);
  }

  // A map field points at a generated ...Entry message, which the page shows as map<key, value> instead.
  const mapEntries = new Set(files.flatMap(f => f.messages.flatMap(m => m.fields.filter(x => x.ismap).map(x => x.fullType))));

  // Each RPC's request and response are drawn under the RPC, so they aren't listed again as messages.
  const methodTypes = new Set(files.flatMap(f => f.services.flatMap(s => s.methods.flatMap(m => [m.requestFullType, m.responseFullType]))));
  const anchored = new Set();
  const anchor = id => (anchored.has(id) ? undefined : (anchored.add(id), id));

  const kebab = name => name.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();

  const typeRef = (fullName, label) => {
    if (types.has(fullName)) return el("a", { href: `#${fullName}` }, el("code", {}, label));
    if (fullName.startsWith("google.protobuf.")) {
      const href = `https://protobuf.dev/reference/protobuf/google.protobuf/#${kebab(fullName.slice(16))}`;
      return el("a", { href, target: "_blank", rel: "noopener" }, el("code", {}, label));
    }
    return el("code", {}, label);
  };

  const fieldType = field => {
    const entry = field.ismap ? types.get(field.fullType) : undefined;
    if (!entry) return typeRef(field.fullType, field.longType);
    const [key, value] = ["key", "value"].map(n => entry.fields.find(f => f.name === n));
    return [el("code", {}, "map<"), typeRef(key.fullType, key.longType), el("code", {}, ", "), typeRef(value.fullType, value.longType), el("code", {}, ">")];
  };

  // proto3 optional is a oneof of one, named _field, which protoc adds itself. It reads as optional, not as a choice.
  const oneofOf = field => (field.isoneof && !field.oneofdecl.startsWith("_") ? field.oneofdecl : "");

  const fieldTable = message => {
    if (!message?.fields.length) return el("p", { class: "none" }, "No fields.");
    const rows = [];
    let group = "";
    for (const field of message.fields) {
      const oneof = oneofOf(field);
      if (oneof && oneof !== group) {
        rows.push(el("tr", { class: "oneof" }, el("td", { colspan: "3" }, "Exactly one of ", el("code", {}, oneof), ":")));
      }
      group = oneof;
      const labels = [field.label === "repeated" && !field.ismap ? "repeated" : "", field.label === "optional" ? "optional" : ""].filter(Boolean);
      rows.push(el("tr", { class: oneof ? "in-oneof" : undefined },
        el("td", {}, el("code", {}, field.name), labels.map(l => el("span", { class: "label" }, l))),
        el("td", {}, fieldType(field)),
        el("td", {},
          deprecated(field) ? el("p", {}, badge("old", "Deprecated")) : null,
          paragraphs(field.description),
          field.defaultValue ? el("p", {}, "Default: ", el("code", {}, field.defaultValue)) : null)));
    }
    return el("div", { class: "table" }, el("table", {},
      el("thead", {}, el("tr", {}, el("th", {}, "Field"), el("th", {}, "Type"), el("th", {}, "Description"))),
      el("tbody", {}, rows)));
  };

  const enumTable = e => el("div", { class: "table" }, el("table", { class: "enum" },
    el("thead", {}, el("tr", {}, el("th", {}, "Value"), el("th", {}, "Number"), el("th", {}, "Description"))),
    el("tbody", {}, e.values.map(v => el("tr", {},
      el("td", {}, el("code", {}, v.name)),
      el("td", {}, el("code", {}, v.number)),
      el("td", {}, deprecated(v) ? el("p", {}, badge("old", "Deprecated")) : null, paragraphs(v.description)))))));

  const streaming = method =>
    method.requestStreaming && method.responseStreaming ? "bidirectional stream"
      : method.requestStreaming ? "client stream"
        : method.responseStreaming ? "server stream" : "";

  const io = (heading, fullName, label) => el("div", { id: anchor(fullName) },
    el("h5", {}, `${heading} `, el("a", { href: `#${fullName}` }, label)),
    paragraphs(types.get(fullName)?.description),
    fieldTable(types.get(fullName)));

  const methodCard = (service, method) => el("article", { class: "card", id: `${service.fullName}.${method.name}` },
    el("h4", {}, method.name, streaming(method) ? badge("stream", streaming(method)) : null, deprecated(method) ? badge("old", "Deprecated") : null),
    el("pre", { class: "signature" },
      `rpc ${method.name}(${method.requestStreaming ? "stream " : ""}${method.requestLongType}) returns (${method.responseStreaming ? "stream " : ""}${method.responseLongType})`),
    el("p", { class: "path" }, "Path ", el("code", {}, `/${service.fullName}/${method.name}`)),
    paragraphs(method.description),
    el("div", { class: "io" },
      io(method.requestStreaming ? "Request, streamed" : "Request", method.requestFullType, method.requestLongType),
      io(method.responseStreaming ? "Response, streamed" : "Response", method.responseFullType, method.responseLongType)));

  const typeCard = (item, body) => el("article", { class: "card", id: anchor(item.fullName) },
    el("h4", {}, item.longName, deprecated(item) ? badge("old", "Deprecated") : null),
    paragraphs(item.description),
    body);

  // Files of one package read as one contract.
  const packages = new Map();
  for (const file of files) {
    if (!packages.has(file.package)) packages.set(file.package, []);
    packages.get(file.package).push(file);
  }

  const toc = [];
  const sections = [];
  for (const [name, pkgFiles] of packages) {
    const services = pkgFiles.flatMap(f => f.services);
    const messages = pkgFiles.flatMap(f => f.messages).filter(m => !mapEntries.has(m.fullName) && !methodTypes.has(m.fullName));
    const enums = pkgFiles.flatMap(f => f.enums);

    sections.push(el("section", { class: "package", id: name },
      // The page's title already names a lone package.
      packages.size > 1 ? el("h2", {}, name) : null,
      el("p", { class: "files" }, pkgFiles.map(f => f.name).join(", ")),
      pkgFiles.flatMap(f => paragraphs(f.description)),
      services.length ? el("h3", { class: "kind" }, "Services") : null,
      services.map(s => el("section", { class: "service", id: s.fullName },
        el("h3", {}, s.name, deprecated(s) ? badge("old", "Deprecated") : null),
        paragraphs(s.description),
        s.methods.map(m => methodCard(s, m)))),
      messages.length ? el("h3", { class: "kind" }, "Messages") : null,
      messages.map(m => typeCard(m, fieldTable(m))),
      enums.length ? el("h3", { class: "kind" }, "Enums") : null,
      enums.map(e => typeCard(e, enumTable(e)))));

    const link = (id, text) => el("a", { href: `#${id}` }, text);
    const leaf = (id, text) => el("li", { "data-name": text.toLowerCase() }, link(id, text));
    toc.push(el("div", { class: "package" }, link(name, name)));
    if (services.length) {
      toc.push(el("div", { class: "group" }, "Services"), el("ul", {}, services.map(s => el("li", { "data-name": s.name.toLowerCase() },
        link(s.fullName, s.name),
        el("ul", {}, s.methods.map(m => leaf(`${s.fullName}.${m.name}`, m.name)))))));
    }
    if (messages.length) toc.push(el("div", { class: "group" }, "Messages"), el("ul", {}, messages.map(m => leaf(m.fullName, m.longName))));
    if (enums.length) toc.push(el("div", { class: "group" }, "Enums"), el("ul", {}, enums.map(e => leaf(e.fullName, e.longName))));
  }

  const count = (n, word) => `${n} ${word}${n === 1 ? "" : "s"}`;
  const all = [...packages.values()].flat();
  const services = all.flatMap(f => f.services);
  const summary = [
    count(services.length, "service"),
    count(services.flatMap(s => s.methods).length, "method"),
    count(all.flatMap(f => f.messages).filter(m => !mapEntries.has(m.fullName)).length, "message"),
    count(all.flatMap(f => f.enums).length, "enum"),
  ].join(" · ");

  document.title = [...packages.keys()].join(", ") || document.title;
  document.getElementById("doc").append(
    el("header", {}, el("h1", {}, document.title), el("p", { class: "summary" }, `gRPC · ${summary}`)),
    ...sections);
  document.getElementById("toc").append(...toc);

  // A service stays in the contents while any of its methods matches.
  document.getElementById("filter").addEventListener("input", event => {
    const query = event.target.value.trim().toLowerCase();
    for (const item of document.querySelectorAll("#toc li")) {
      const own = item.dataset.name.includes(query);
      const child = [...item.querySelectorAll("li")].some(li => li.dataset.name.includes(query));
      item.hidden = query !== "" && !own && !child;
    }
  });
})();
