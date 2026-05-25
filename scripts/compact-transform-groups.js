#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");

const AXES = ["X", "Y", "Z"];
const DOMAINS = ["translation", "rotation", "scale"];

function isObject(v) {
    return v && typeof v === "object" && !Array.isArray(v);
}

function isNumber(v) {
    return typeof v === "number" && Number.isFinite(v);
}

function clone(v) {
    return JSON.parse(JSON.stringify(v));
}

function deepEqual(a, b) {
    if (a === b) return true;
    if (typeof a !== typeof b) return false;
    if (Array.isArray(a)) {
        if (!Array.isArray(b) || a.length !== b.length) return false;
        for (let i = 0; i < a.length; i++) {
            if (!deepEqual(a[i], b[i])) return false;
        }
        return true;
    }
    if (isObject(a)) {
        if (!isObject(b)) return false;
        const ak = Object.keys(a);
        const bk = Object.keys(b);
        if (ak.length !== bk.length) return false;
        for (const k of ak) {
            if (!deepEqual(a[k], b[k])) return false;
        }
        return true;
    }
    return false;
}

function domainKeys(domain) {
    return [domain, domain + "X", domain + "Y", domain + "Z"];
}

function hasDomainKey(obj, domain) {
    return domainKeys(domain).some((k) => Object.prototype.hasOwnProperty.call(obj, k));
}

function getAxesFromTransform(obj, domain, warnings, contextLabel) {
    const out = [undefined, undefined, undefined];
    if (!isObject(obj)) return out;

    const vec = obj[domain];
    if (domain === "scale") {
        if (isNumber(vec)) {
            out[0] = vec; out[1] = vec; out[2] = vec;
        } else if (Array.isArray(vec) && vec.length === 3 && vec.every(isNumber)) {
            out[0] = vec[0]; out[1] = vec[1]; out[2] = vec[2];
        }
    } else {
        if (Array.isArray(vec) && vec.length === 3 && vec.every(isNumber)) {
            out[0] = vec[0]; out[1] = vec[1]; out[2] = vec[2];
        }
    }

    for (let i = 0; i < 3; i++) {
        const k = domain + AXES[i];
        const v = obj[k];
        if (isNumber(v)) {
            out[i] = v;
        } else if (Array.isArray(v) && v.length === 3 && v.every(isNumber)) {
            warnings.push("Warning: " + contextLabel + " has malformed " + k + " array. Treated as " + domain + ".");
            out[0] = v[0]; out[1] = v[1]; out[2] = v[2];
        }
    }

    return out;
}

function applyOverrideAxes(parentAxes, overrideObj, domain, warnings, contextLabel) {
    const out = [parentAxes[0], parentAxes[1], parentAxes[2]];

    const vec = overrideObj[domain];
    if (domain === "scale") {
        if (isNumber(vec)) {
            out[0] = vec; out[1] = vec; out[2] = vec;
        } else if (Array.isArray(vec) && vec.length === 3 && vec.every(isNumber)) {
            out[0] = vec[0]; out[1] = vec[1]; out[2] = vec[2];
        }
    } else {
        if (Array.isArray(vec) && vec.length === 3 && vec.every(isNumber)) {
            out[0] = vec[0]; out[1] = vec[1]; out[2] = vec[2];
        }
    }

    for (let i = 0; i < 3; i++) {
        const k = domain + AXES[i];
        const v = overrideObj[k];
        if (isNumber(v)) {
            out[i] = v;
        } else if (Array.isArray(v) && v.length === 3 && v.every(isNumber)) {
            warnings.push("Warning: " + contextLabel + " has malformed " + k + " array. Treated as " + domain + ".");
            out[0] = v[0]; out[1] = v[1]; out[2] = v[2];
        }
    }

    return out;
}

function mergeTransforms(base, over) {
    const m = clone(base || {});
    for (const [k, v] of Object.entries(over || {})) {
        if (k === "id") continue;
        m[k] = clone(v);
    }
    m.id = over.id;
    return m;
}

function toTransformMap(arr) {
    const m = new Map();
    for (const t of arr || []) {
        if (isObject(t) && typeof t.id === "string") {
            m.set(t.id, clone(t));
        }
    }
    return m;
}

function resolveEffectiveGroup(groupName, groups, cache, stack) {
    if (cache.has(groupName)) return cache.get(groupName);
    if (stack.includes(groupName)) {
        throw new Error("Circular extends detected: " + stack.concat(groupName).join(" -> "));
    }

    const g = groups[groupName];
    if (g === undefined) {
        throw new Error("Missing transform group: " + groupName);
    }

    let map = new Map();
    if (Array.isArray(g)) {
        map = toTransformMap(g);
    } else if (isObject(g)) {
        if (typeof g.extends === "string" && g.extends.length > 0) {
            const parentMap = resolveEffectiveGroup(g.extends, groups, cache, stack.concat(groupName));
            map = new Map();
            for (const [id, tr] of parentMap.entries()) {
                map.set(id, clone(tr));
            }
        }

        if (Array.isArray(g.overrides)) {
            for (const over of g.overrides) {
                if (!isObject(over) || typeof over.id !== "string") continue;
                const prev = map.get(over.id) || { id: over.id };
                map.set(over.id, mergeTransforms(prev, over));
            }
        }

        if (Array.isArray(g.appends)) {
            for (const ap of g.appends) {
                if (!isObject(ap) || typeof ap.id !== "string") continue;
                map.set(ap.id, clone(ap));
            }
        }
    }

    cache.set(groupName, map);
    return map;
}

function compactOverrideEntry(overrideEntry, parentEntry, warnings, label) {
    const out = { id: overrideEntry.id };

    const transformKeys = new Set();
    for (const d of DOMAINS) {
        for (const k of domainKeys(d)) transformKeys.add(k);
    }

    for (const [k, v] of Object.entries(overrideEntry)) {
        if (k === "id") continue;
        if (transformKeys.has(k)) continue;

        const parentVal = parentEntry ? parentEntry[k] : undefined;
        if (!deepEqual(v, parentVal)) {
            out[k] = clone(v);
        }
    }

    for (const domain of DOMAINS) {
        if (!hasDomainKey(overrideEntry, domain)) continue;

        const parentAxes = getAxesFromTransform(parentEntry || {}, domain, warnings, label + " parent");
        const finalAxes = applyOverrideAxes(parentAxes, overrideEntry, domain, warnings, label + " override");

        const changed = [false, false, false];
        let changedCount = 0;
        for (let i = 0; i < 3; i++) {
            if (!Object.is(finalAxes[i], parentAxes[i])) {
                changed[i] = true;
                changedCount++;
            }
        }

        if (changedCount === 3) {
            if (domain === "scale" && finalAxes[0] === finalAxes[1] && finalAxes[1] === finalAxes[2]) {
                out[domain] = finalAxes[0];
            } else {
                out[domain] = [finalAxes[0], finalAxes[1], finalAxes[2]];
            }
            continue;
        }

        for (let i = 0; i < 3; i++) {
            if (changed[i]) {
                out[domain + AXES[i]] = finalAxes[i];
            }
        }
    }

    return out;
}

function compactFile(filePath, write) {
    const raw = fs.readFileSync(filePath, "utf8");
    const doc = JSON.parse(raw);

    const warnings = [];
    let removedProps = 0;
    let removedOverrides = 0;

    const targets = collectTransformGroupContainers(doc);
    if (targets.length === 0) {
        console.log("Skip (no transformGroups): " + filePath);
        return { changed: false, removedProps: 0, removedOverrides: 0, warnings: [] };
    }

    for (const t of targets) {
        const cache = new Map();
        const result = compactTransformGroups(t.groups, cache, warnings);
        removedProps += result.removedProps;
        removedOverrides += result.removedOverrides;
    }

    const nextRaw = JSON.stringify(doc, null, 2) + "\n";
    const next = ensureDecimalForIntegers(nextRaw);
    const changed = next !== raw;

    if (changed && write) {
        fs.writeFileSync(filePath, next, "utf8");
    }

    return { changed, removedProps, removedOverrides, warnings };
}

function collectJsonFiles(targetPath) {
    const st = fs.statSync(targetPath);
    if (st.isFile()) return [targetPath];

    const out = [];
    const stack = [targetPath];
    while (stack.length) {
        const d = stack.pop();
        for (const name of fs.readdirSync(d)) {
            const full = path.join(d, name);
            const s = fs.statSync(full);
            if (s.isDirectory()) {
                stack.push(full);
            } else if (s.isFile() && name.toLowerCase().endsWith(".json")) {
                out.push(full);
            }
        }
    }
    return out;
}

function parseArgs(argv) {
    const args = { file: null, dir: null, write: false };
    for (let i = 2; i < argv.length; i++) {
        const a = argv[i];
        if (a === "--file") args.file = argv[++i];
        else if (a === "--dir") args.dir = argv[++i];
        else if (a === "--write") args.write = true;
    }
    if (!args.file && !args.dir) {
        throw new Error("Use --file <path> or --dir <path> [--write]");
    }
    return args;
}

function ensureDecimalForIntegers(jsonText) {
    let out = "";
    let i = 0;
    let inString = false;
    let escaping = false;

    while (i < jsonText.length) {
        const ch = jsonText[i];

        if (inString) {
            out += ch;
            if (escaping) {
                escaping = false;
            } else if (ch === "\\") {
                escaping = true;
            } else if (ch === "\"") {
                inString = false;
            }
            i++;
            continue;
        }

        if (ch === "\"") {
            inString = true;
            out += ch;
            i++;
            continue;
        }

        // Number token start (outside strings)
        if (ch === "-" || (ch >= "0" && ch <= "9")) {
            const start = i;
            i++;
            while (i < jsonText.length) {
                const c = jsonText[i];
                const isNumChar =
                    (c >= "0" && c <= "9") || c === "." || c === "e" || c === "E" || c === "+" || c === "-";
                if (!isNumChar) break;
                i++;
            }

            let token = jsonText.slice(start, i);

            // Only whole integers get ".0"
            if (/^-?\d+$/.test(token)) {
                token += ".0";
            }

            out += token;
            continue;
        }

        out += ch;
        i++;
    }

    return out;
}

function walkJson(node, visitor, path = [], parent = null, parentKey = null) {
    visitor(node, path, parent, parentKey);

    if (Array.isArray(node)) {
        for (let i = 0; i < node.length; i++) {
            walkJson(node[i], visitor, path.concat(i), node, i);
        }
        return;
    }

    if (isObject(node)) {
        for (const [k, v] of Object.entries(node)) {
            walkJson(v, visitor, path.concat(k), node, k);
        }
    }
}

function collectTransformGroupContainers(root) {
    const found = [];
    walkJson(root, (node, path) => {
        if (isObject(node) && isObject(node.transformGroups)) {
            found.push({
                container: node,
                groups: node.transformGroups,
                path
            });
        }
    });
    return found;
}

function compactTransformGroups(groups, cache, warnings) {
    let removedProps = 0;
    let removedOverrides = 0;

    for (const [groupName, groupVal] of Object.entries(groups)) {
        if (!isObject(groupVal)) continue;
        if (typeof groupVal.extends !== "string") continue;
        if (!Array.isArray(groupVal.overrides)) continue;

        const parentMap = resolveEffectiveGroup(groupVal.extends, groups, cache, []);
        const nextOverrides = [];

        for (let i = 0; i < groupVal.overrides.length; i++) {
            const over = groupVal.overrides[i];
            if (!isObject(over) || typeof over.id !== "string") {
                nextOverrides.push(over);
                continue;
            }

            const parentEntry = parentMap.get(over.id);
            if (!parentEntry) {
                nextOverrides.push(over);
                continue;
            }

            const compacted = compactOverrideEntry(over, parentEntry, warnings, groupName + "[" + i + "]");
            const beforeKeys = Object.keys(over).length;
            const afterKeys = Object.keys(compacted).length;
            if (afterKeys < beforeKeys) removedProps += (beforeKeys - afterKeys);

            if (afterKeys > 1) nextOverrides.push(compacted);
            else removedOverrides++;
        }

        groupVal.overrides = nextOverrides;
        if (groupVal.overrides.length === 0) {
            delete groupVal.overrides;
        }
    }

    return { removedProps, removedOverrides };
}

function main() {
    const args = parseArgs(process.argv);
    const targets = args.file ? collectJsonFiles(args.file) : collectJsonFiles(args.dir);

    let changedCount = 0;
    let propDrops = 0;
    let overrideDrops = 0;
    const allWarnings = [];

    for (const f of targets) {
        try {
            const r = compactFile(f, args.write);
            if (r.changed) changedCount++;
            propDrops += r.removedProps;
            overrideDrops += r.removedOverrides;
            for (const w of r.warnings) allWarnings.push(path.relative(process.cwd(), f) + ": " + w);
            console.log((r.changed ? "Changed" : "Unchanged") + ": " + f);
        } catch (e) {
            console.error("Error in " + f + ": " + e.message);
        }
    }

    console.log("Summary: files changed=" + changedCount + ", properties removed=" + propDrops + ", empty overrides removed=" + overrideDrops);
    if (allWarnings.length) {
        console.log("Warnings:");
        for (const w of allWarnings) console.log("  - " + w);
    }
    if (!args.write) {
        console.log("Dry run only. Add --write to save changes.");
    }
}

main();