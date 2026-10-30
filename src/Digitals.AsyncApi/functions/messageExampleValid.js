// dt-message-example-valid: every example of an AsyncAPI 3 message matches its payload and headers schemas.
// Spectral's asyncapi-message-examples checks AsyncAPI 2 only. It checks the keywords a contract uses; any
// other keyword is skipped, never failed. Spectral hands the message over with every $ref resolved.
// check() is a copy of the one in Digitals.OpenApi's exampleMatchesSchema.js: change both together.

var formats = {
  uuid: /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/,
  "date-time": /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/,
  date: /^\d{4}-\d{2}-\d{2}$/,
  time: /^\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?$/,
  email: /^[^@\s]+@[^@\s]+\.[^@\s]+$/,
  uri: /^[a-zA-Z][a-zA-Z0-9+.-]*:\S*$/,
};

function typeOf(value) {
  if (value === null) return "null";
  if (Array.isArray(value)) return "array";
  if (typeof value === "number") return Number.isInteger(value) ? "integer" : "number";
  return typeof value;
}

function fitsType(value, type) {
  var actual = typeOf(value);
  return actual === type || (type === "number" && actual === "integer");
}

function length(text) {
  return Array.from(text).length;
}

// Returns the first few ways value breaks schema, each as "where: what".
function check(value, schema, where, errors, depth) {
  if (!schema || typeof schema !== "object" || depth > 32 || errors.length >= 5) return;
  if (value === null && schema.nullable === true) return;

  if (schema.type !== undefined) {
    var types = Array.isArray(schema.type) ? schema.type : [schema.type];
    if (!types.some(function (t) { return fitsType(value, t); })) {
      errors.push(where + ": is " + typeOf(value) + ", the schema wants " + types.join(" or "));
      return;
    }
  }

  if (schema.const !== undefined && JSON.stringify(schema.const) !== JSON.stringify(value)) {
    errors.push(where + ": must be " + JSON.stringify(schema.const));
  }
  if (Array.isArray(schema.enum) && !schema.enum.some(function (e) { return JSON.stringify(e) === JSON.stringify(value); })) {
    errors.push(where + ": " + JSON.stringify(value) + " is not one of " + JSON.stringify(schema.enum));
  }

  ["allOf"].forEach(function (key) {
    (schema[key] || []).forEach(function (part) { check(value, part, where, errors, depth + 1); });
  });
  ["anyOf", "oneOf"].forEach(function (key) {
    var options = schema[key];
    if (!Array.isArray(options) || options.length === 0) return;
    var fits = options.some(function (option) {
      var found = [];
      check(value, option, where, found, depth + 1);
      return found.length === 0;
    });
    if (!fits) errors.push(where + ": matches none of the " + key + " schemas");
  });

  var kind = typeOf(value);
  if (kind === "string") {
    if (schema.maxLength !== undefined && length(value) > schema.maxLength) {
      errors.push(where + ": is " + length(value) + " characters, over the maxLength of " + schema.maxLength);
    }
    if (schema.minLength !== undefined && length(value) < schema.minLength) {
      errors.push(where + ": is " + length(value) + " characters, under the minLength of " + schema.minLength);
    }
    if (schema.pattern !== undefined) {
      var pattern = null;
      try { pattern = new RegExp(schema.pattern, "u"); } catch (e) {
        try { pattern = new RegExp(schema.pattern); } catch (e2) { pattern = null; }
      }
      if (pattern && !pattern.test(value)) {
        errors.push(where + ": " + JSON.stringify(value) + " doesn't match the pattern " + schema.pattern);
      }
    }
    if (formats[schema.format] && !formats[schema.format].test(value)) {
      errors.push(where + ": " + JSON.stringify(value) + " is not a " + schema.format);
    }
  }

  if (kind === "integer" || kind === "number") {
    var min = schema.minimum, max = schema.maximum;
    if (min !== undefined && (schema.exclusiveMinimum === true ? value <= min : value < min)) {
      errors.push(where + ": " + value + " is below the minimum of " + min);
    }
    if (max !== undefined && (schema.exclusiveMaximum === true ? value >= max : value > max)) {
      errors.push(where + ": " + value + " is above the maximum of " + max);
    }
    if (typeof schema.exclusiveMinimum === "number" && value <= schema.exclusiveMinimum) {
      errors.push(where + ": " + value + " must be above " + schema.exclusiveMinimum);
    }
    if (typeof schema.exclusiveMaximum === "number" && value >= schema.exclusiveMaximum) {
      errors.push(where + ": " + value + " must be below " + schema.exclusiveMaximum);
    }
    if (schema.format === "int32" && (value < -2147483648 || value > 2147483647)) {
      errors.push(where + ": " + value + " doesn't fit an int32");
    }
  }

  if (kind === "array") {
    if (schema.maxItems !== undefined && value.length > schema.maxItems) {
      errors.push(where + ": has " + value.length + " items, over the maxItems of " + schema.maxItems);
    }
    if (schema.minItems !== undefined && value.length < schema.minItems) {
      errors.push(where + ": has " + value.length + " items, under the minItems of " + schema.minItems);
    }
    if (schema.items && typeof schema.items === "object") {
      value.forEach(function (item, i) { check(item, schema.items, where + "[" + i + "]", errors, depth + 1); });
    }
  }

  if (kind === "object") {
    var properties = schema.properties || {};
    (schema.required || []).forEach(function (name) {
      if (value[name] === undefined) errors.push(where + ": lacks the required field '" + name + "'");
    });
    var count = 0;
    Object.keys(value).forEach(function (name) {
      count++;
      if (properties[name] !== undefined) {
        check(value[name], properties[name], where + "." + name, errors, depth + 1);
      } else if (schema.additionalProperties === false) {
        errors.push(where + ": has '" + name + "', which the schema doesn't define");
      } else if (schema.additionalProperties && typeof schema.additionalProperties === "object") {
        check(value[name], schema.additionalProperties, where + "." + name, errors, depth + 1);
      }
    });
    if (schema.maxProperties !== undefined && count > schema.maxProperties) {
      errors.push(where + ": has " + count + " fields, over the maxProperties of " + schema.maxProperties);
    }
  }
}

// A payload is a schema, or { schemaFormat, schema } for a schema in another format.
function schemaOf(node) {
  if (node && typeof node === "object" && node.schemaFormat !== undefined) {
    return /json|asyncapi/i.test(node.schemaFormat) ? node.schema : undefined;
  }
  return node;
}

export default function messageExampleValid(message, _options, context) {
  var results = [];
  if (!message || typeof message !== "object" || !Array.isArray(message.examples)) return results;

  message.examples.forEach(function (example, i) {
    if (!example || typeof example !== "object") return;
    ["payload", "headers"].forEach(function (part) {
      var schema = schemaOf(message[part]);
      if (example[part] === undefined || !schema) return;
      var errors = [];
      check(example[part], schema, part, errors, 0);
      if (errors.length > 0) {
        results.push({
          message: "The example doesn't match its schema. " + errors.join("; "),
          path: context.path.concat(["examples", i, part]),
        });
      }
    });
  });
  return results;
}
