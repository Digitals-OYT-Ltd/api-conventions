// dt-error-example: every 4xx and 5xx body has an example of its own whose status is that response's
// code. Without one, the docs show the schema's example, which belongs to some other status, so a 401
// ends up showing a 404 body. vacuum hands the responses map over with every $ref resolved.

function getSchema() {
  return {
    name: "errorExample",
    description: "An error response has its own example, with status equal to the response code",
  };
}

function examplesOf(body) {
  if (body.example !== undefined) return [body.example];
  var named = body.examples || {};
  return Object.keys(named)
    .map(function (name) { return (named[name] || {}).value; })
    .filter(function (value) { return value !== undefined; });
}

function runRule(input) {
  var results = [];
  if (!input || typeof input !== "object") return results;

  Object.keys(input).forEach(function (code) {
    if (!/^[45]\d\d$/.test(code)) return;
    var content = (input[code] || {}).content || {};
    Object.keys(content).forEach(function (media) {
      var examples = examplesOf(content[media] || {});
      if (examples.length === 0) {
        results.push({ message: "The " + code + " response has no example of its own, so the docs show the schema's. Add one with status " + code });
        return;
      }
      examples.forEach(function (example) {
        var status = example && typeof example === "object" ? example.status : undefined;
        if (status !== undefined && Number(status) !== Number(code)) {
          results.push({ message: "The " + code + " response's example has status " + status + ". Make it " + code });
        }
      });
    });
  });
  return results;
}
