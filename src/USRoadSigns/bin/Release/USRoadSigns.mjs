/*
 * US Road Signs - UI module (Cities: Skylines II).
 *
 * Adds the "Message Sign" section to the selected info panel for digital message signs and arrow boards,
 * and the "Route sign" section for route marker assemblies and destination signs.
 * Plain ES module, no build step: the game exposes React and its cs2/* modules as window globals (the same
 * externals the official mod UI template compiles against).
 *
 * C# counterparts: USRoadSigns.Digital.DigitalSignSection and USRoadSigns.Route.RouteSignSection (properties and triggers).
 */

const React = window.React;
const api = window["cs2/api"];
const modding = window["cs2/modding"];

const h = React.createElement;
const GROUP = "USRoadSigns";
const SECTION = "USRoadSigns.Digital.DigitalSignSection";
const ROUTE_SECTION = "USRoadSigns.Route.RouteSignSection";
const LINE_SEPARATOR = "\n";
const PAGE_SEPARATOR = "\f";

function tryGetModule(path, name) {
    try {
        return modding.getModule(path, name);
    } catch (e) {
        console.warn("USRoadSigns: game UI module not found: " + path + " " + name);
        return null;
    }
}

const InfoSection = tryGetModule("game-ui/game/components/selected-info-panel/shared-components/info-section/info-section.tsx", "InfoSection");
const InfoRow = tryGetModule("game-ui/game/components/selected-info-panel/shared-components/info-row/info-row.tsx", "InfoRow");

function send(name, ...args) {
    api.trigger(GROUP, name, ...args);
}

// ---------------------------------------------------------------------------------------------
// Styles (inline, so the module needs no stylesheet)
// ---------------------------------------------------------------------------------------------

const S = {
    body: { padding: "6rem 10rem 10rem 10rem", display: "flex", flexDirection: "column" },
    label: { fontSize: "12rem", opacity: 0.75, textTransform: "uppercase", margin: "8rem 0 4rem 0" },
    pageHeader: { display: "flex", alignItems: "center", justifyContent: "space-between", margin: "6rem 0 3rem 0" },
    pageTitle: { fontSize: "12rem", opacity: 0.75, textTransform: "uppercase" },
    lineInput: {
        width: "100%",
        boxSizing: "border-box",
        margin: "2rem 0",
        padding: "4rem 8rem",
        fontFamily: "monospace",
        fontSize: "16rem",
        letterSpacing: "2rem",
        textTransform: "uppercase",
        color: "rgb(255, 178, 60)",
        backgroundColor: "rgba(10, 10, 10, 0.85)",
        border: "2rem solid rgba(255, 255, 255, 0.15)",
        borderRadius: "3rem",
    },
    lineInputOverflow: { borderColor: "rgb(230, 60, 50)" },
    row: { display: "flex", flexWrap: "wrap", alignItems: "center", margin: "2rem 0" },
    button: {
        padding: "3rem 9rem",
        margin: "2rem 4rem 2rem 0",
        fontSize: "13rem",
        color: "rgba(255, 255, 255, 0.9)",
        backgroundColor: "rgba(255, 255, 255, 0.1)",
        border: "1rem solid rgba(255, 255, 255, 0.2)",
        borderRadius: "3rem",
        cursor: "pointer",
    },
    buttonSelected: { backgroundColor: "rgba(80, 160, 255, 0.55)", borderColor: "rgba(140, 200, 255, 0.9)" },
    swatch: { width: "22rem", height: "22rem", margin: "2rem 6rem 2rem 0", borderRadius: "11rem", border: "2rem solid rgba(255,255,255,0.25)", cursor: "pointer" },
    swatchSelected: { border: "3rem solid white" },
    hint: { fontSize: "11rem", opacity: 0.6, margin: "4rem 0" },
    value: { minWidth: "48rem", textAlign: "center", fontSize: "13rem" },
};

function merge(...styles) {
    return Object.assign({}, ...styles.filter(Boolean));
}

function Button(props) {
    return h("button", {
        style: merge(S.button, props.selected && S.buttonSelected, props.style),
        onClick: props.onClick,
        disabled: props.disabled,
    }, props.children);
}

// ---------------------------------------------------------------------------------------------
// Section
// ---------------------------------------------------------------------------------------------

function clonePages(pages, lines) {
    const list = (pages || []).map(p => {
        const copy = p.slice(0, lines);
        while (copy.length < lines) copy.push("");
        return copy;
    });
    if (list.length === 0) list.push(new Array(lines).fill(""));
    return list;
}

function encode(pages) {
    return pages.map(p => p.join(LINE_SEPARATOR)).join(PAGE_SEPARATOR);
}

function MessageEditor(props) {
    const lines = props.lines;
    const [pages, setPages] = React.useState(() => clonePages(props.pages, lines));

    // Reload the fields when another sign is selected or C# replaced the message (reset).
    React.useEffect(() => {
        setPages(clonePages(props.pages, lines));
    }, [props.signKey]);

    const commit = (next) => {
        setPages(next);
        send("digitalSetMessage", encode(next));
    };

    // Upper-casing changes the input's value, which makes the browser put the caret at the end.
    // Remember where it was and put it back after React re-renders the field.
    const pendingCaret = React.useRef(null);
    React.useLayoutEffect(() => {
        const pending = pendingCaret.current;
        if (pending && pending.input) {
            try { pending.input.setSelectionRange(pending.start, pending.end); } catch (e) { }
        }
        pendingCaret.current = null;
    });

    const setLine = (p, l, input) => {
        pendingCaret.current = { input: input, start: input.selectionStart, end: input.selectionEnd };
        const next = pages.map(page => page.slice());
        next[p][l] = input.value.toUpperCase().replace(/[\n\f\r]/g, "");
        commit(next);
    };

    const addPage = () => {
        if (pages.length >= props.maxPages) return;
        commit(pages.concat([new Array(lines).fill("")]));
    };

    const removePage = (p) => {
        if (pages.length <= 1) return;
        commit(pages.filter((_, i) => i !== p));
    };

    const clearAll = () => commit([new Array(lines).fill("")]);

    const overflow = props.overflow || [];
    const children = [];
    pages.forEach((page, p) => {
        children.push(h("div", { key: "ph" + p, style: S.pageHeader },
            h("div", { style: S.pageTitle }, pages.length > 1 ? "Page " + (p + 1) : "Message"),
            pages.length > 1 ? h(Button, { onClick: () => removePage(p) }, "Remove") : null));
        page.forEach((line, l) => {
            const tooLong = overflow[p] && overflow[p][l];
            children.push(h("input", {
                key: "in" + p + "_" + l,
                type: "text",
                value: line,
                maxLength: props.maxLineLength,
                style: merge(S.lineInput, tooLong && S.lineInputOverflow),
                placeholder: "Line " + (l + 1),
                onChange: (e) => setLine(p, l, e.target),
                onKeyDown: (e) => {
                    if (e.key === "Enter" || e.key === "Escape") e.currentTarget.blur();
                },
            }));
        });
    });

    const anyOverflow = overflow.some(page => page && page.some(Boolean));

    return h("div", null,
        children,
        anyOverflow ? h("div", { style: merge(S.hint, { color: "rgb(255, 120, 100)", opacity: 1 }) }, "A red line is too wide for this sign and gets cut off.") : null,
        h("div", { style: S.row },
            pages.length < props.maxPages ? h(Button, { onClick: addPage }, "+ Add page") : null,
            h(Button, { onClick: clearAll }, "Clear")),
        h("div", { style: S.hint }, "Arrows: < left, > right, ^ up, ~ down.")
    );
}

function DigitalSignSection(props) {
    const isText = props.kind === "text";

    const header = InfoRow
        ? h(InfoRow, { left: isText ? "Message sign" : "Arrow board", right: props.matrix + (isText ? " LEDs" : " lamps"), uppercase: true, disableFocus: true })
        : h("div", { style: S.label }, isText ? "Message sign" : "Arrow board");

    const body = [];

    if (isText) {
        body.push(h(MessageEditor, {
            key: "editor",
            signKey: props.signKey,
            pages: props.pages,
            overflow: props.overflow,
            lines: props.lines,
            maxPages: props.maxPages,
            maxLineLength: props.maxLineLength,
        }));

        if ((props.pages || []).length > 1) {
            const seconds = (props.pageMs / 1000).toFixed(1);
            body.push(h("div", { key: "pt", style: S.label }, "Time per page"));
            body.push(h("div", { key: "ptr", style: S.row },
                h(Button, { onClick: () => send("digitalSetPageMs", Math.max(500, props.pageMs - 500)) }, "-"),
                h("div", { style: S.value }, seconds + " s"),
                h(Button, { onClick: () => send("digitalSetPageMs", Math.min(10000, props.pageMs + 500)) }, "+")));
        }

        body.push(h("div", { key: "al", style: S.label }, "Alignment"));
        body.push(h("div", { key: "alr", style: S.row },
            ["Center", "Left", "Right"].map((name, i) =>
                h(Button, { key: name, selected: props.align === i, onClick: () => send("digitalSetAlign", i) }, name)),
            h(Button, { selected: props.flash, onClick: () => send("digitalSetFlash", !props.flash), style: { marginLeft: "12rem" } }, props.flash ? "Flashing: on" : "Flashing: off")));
    } else {
        body.push(h("div", { key: "am", style: S.label }, "Display"));
        body.push(h("div", { key: "amr", style: S.row },
            (props.arrowModes || []).map((name, i) =>
                h(Button, { key: name, selected: props.arrow === i, onClick: () => send("digitalSetArrow", i) }, name))));
    }

    body.push(h("div", { key: "cl", style: S.label }, "LED color"));
    body.push(h("div", { key: "clr", style: S.row },
        (props.colors || []).map((name, i) =>
            h("div", {
                key: name,
                title: name,
                style: merge(S.swatch, { backgroundColor: props.colorSwatches[i] }, props.color === i && S.swatchSelected),
                onClick: () => send("digitalSetColor", i),
            }))));

    if (props.hasBeacons) {
        body.push(h("div", { key: "bc", style: S.row },
            h(Button, { selected: props.beacons, onClick: () => send("digitalSetBeacons", !props.beacons) }, props.beacons ? "Warning beacons: flashing" : "Warning beacons: off")));
    }

    body.push(h("div", { key: "rs", style: merge(S.row, { marginTop: "8rem" }) },
        h(Button, { onClick: () => send("digitalReset") }, "Reset to default")));

    const content = h("div", { style: S.body }, body);
    return InfoSection
        ? h(InfoSection, { disableFocus: true }, header, content)
        : h("div", null, header, content);
}

// ---------------------------------------------------------------------------------------------
// Route sign section
// ---------------------------------------------------------------------------------------------

/** Option lists arrive flattened as [value, label, value, label, ...]. */
function pairs(flat) {
    const list = [];
    for (let i = 0; i + 1 < (flat || []).length; i += 2) list.push({ value: parseInt(flat[i], 10), label: flat[i + 1] });
    return list;
}

function routeSet(target, index, field, value) {
    send("routeSet", [target, index, field, String(value)].join("\n"));
}

/** Text box with its own state, reloaded when another sign is selected or the sign is reset. */
function TextField(props) {
    const [value, setValue] = React.useState(props.value || "");
    React.useEffect(() => { setValue(props.value || ""); }, [props.signKey]);

    // Upper-casing changes the value, which moves the caret to the end; put it back after rendering.
    const pendingCaret = React.useRef(null);
    React.useLayoutEffect(() => {
        const pending = pendingCaret.current;
        if (pending && pending.input) {
            try { pending.input.setSelectionRange(pending.start, pending.end); } catch (e) { }
        }
        pendingCaret.current = null;
    });

    return h("input", {
        type: "text",
        value: value,
        maxLength: props.maxLength,
        placeholder: props.placeholder,
        style: merge(S.lineInput, { textTransform: props.upper ? "uppercase" : "none", color: "rgb(235, 240, 235)", letterSpacing: "1rem" }, props.style),
        onChange: (e) => {
            const input = e.target;
            pendingCaret.current = { input: input, start: input.selectionStart, end: input.selectionEnd };
            let next = input.value.replace(/[\n\r\f,;|~]/g, "");
            if (props.upper) next = next.toUpperCase();
            setValue(next);
            props.onCommit(next);
        },
        onKeyDown: (e) => {
            if (e.key === "Enter" || e.key === "Escape") e.currentTarget.blur();
        },
    });
}

function OptionRow(props) {
    return h("div", { style: S.row },
        props.options.map(o => h(Button, { key: o.value, selected: props.value === o.value, onClick: () => props.onPick(o.value) }, o.label)));
}

const COUNTY = 5;

function RouteColumnEditor(props) {
    const c = props.column;
    const i = props.index;
    const set = (field, value) => routeSet("column", i, field, value);
    const body = [];
    if (props.count > 1) body.push(h("div", { key: "t", style: S.pageHeader }, h("div", { style: S.pageTitle }, "Route " + (i + 1) + (i === 0 ? " (left)" : (i === props.count - 1 ? " (right)" : "")))));
    body.push(h("div", { key: "ml", style: S.label }, "Route marker"));
    body.push(h(OptionRow, { key: "m", options: props.shieldOptions, value: c.shield, onPick: v => set("shield", v) }));
    body.push(h("div", { key: "nl", style: S.label }, "Route number"));
    body.push(h(TextField, { key: "n", signKey: props.signKey, value: c.number, maxLength: props.maxNumber, upper: true, placeholder: "e.g. 80", onCommit: v => set("number", v) }));
    if (c.shield === COUNTY) {
        body.push(h("div", { key: "cl", style: S.label }, "County name"));
        body.push(h(TextField, { key: "c", signKey: props.signKey, value: c.name, maxLength: props.maxName, upper: true, placeholder: "e.g. LASSEN", onCommit: v => set("name", v) }));
    }
    if (props.hasAux) {
        body.push(h("div", { key: "al", style: S.label }, "Top plate"));
        body.push(h(OptionRow, { key: "a", options: props.auxOptions, value: c.aux, onPick: v => set("aux", v) }));
    }
    body.push(h("div", { key: "dl", style: S.label }, "Direction"));
    body.push(h(OptionRow, { key: "d", options: props.cardinalOptions, value: c.cardinal, onPick: v => set("cardinal", v) }));
    if (props.hasArrow) {
        body.push(h("div", { key: "rl", style: S.label }, "Arrow"));
        body.push(h(OptionRow, { key: "r", options: props.arrowOptions, value: c.arrow, onPick: v => set("arrow", v) }));
    }
    return h("div", { style: { marginBottom: "6rem" } }, body);
}

function DestinationLineEditor(props) {
    const l = props.line;
    const i = props.index;
    const set = (field, value) => routeSet("line", i, field, value);
    return h("div", { style: { marginBottom: "4rem" } },
        h("div", { style: S.pageHeader }, h("div", { style: S.pageTitle }, "Line " + (i + 1))),
        h(TextField, { signKey: props.signKey, value: l.text, maxLength: props.maxText, placeholder: "Destination", onCommit: v => set("text", v) }),
        h(OptionRow, { options: props.arrowOptions, value: l.arrow, onPick: v => set("arrow", v) }),
        h("div", { style: S.row },
            h("div", { style: merge(S.hint, { marginRight: "8rem" }) }, "Miles"),
            h(TextField, { signKey: props.signKey, value: l.distance, maxLength: props.maxDistance, placeholder: "-", style: { width: "70rem" }, onCommit: v => set("distance", v) })));
}

function RouteSignSection(props) {
    const isMarker = props.kind === "marker";
    const header = InfoRow
        ? h(InfoRow, { left: isMarker ? "Route marker" : "Destination sign", right: isMarker ? (props.columns || []).length + " route(s)" : (props.lines || []).length + " line(s)", uppercase: true, disableFocus: true })
        : h("div", { style: S.label }, isMarker ? "Route marker" : "Destination sign");

    const body = [];
    if (isMarker) {
        const columns = props.columns || [];
        columns.forEach((c, i) => body.push(h(RouteColumnEditor, {
            key: "c" + i, index: i, count: columns.length, column: c, signKey: props.signKey,
            hasAux: props.hasAux, hasArrow: props.hasArrow, maxNumber: props.maxNumber, maxName: props.maxName,
            shieldOptions: pairs(props.shieldOptions), auxOptions: pairs(props.auxOptions),
            cardinalOptions: pairs(props.cardinalOptions), arrowOptions: pairs(props.arrowOptions),
        })));
        body.push(h("div", { key: "pl", style: S.label }, "Plate colors"));
        body.push(h(OptionRow, { key: "p", options: [{ value: 0, label: "Match the marker" }, { value: 1, label: "Black on white" }], value: props.plates, onPick: v => routeSet("sign", 0, "plates", v) }));
    } else {
        (props.lines || []).forEach((l, i) => body.push(h(DestinationLineEditor, {
            key: "l" + i, index: i, line: l, signKey: props.signKey, maxText: props.maxText, maxDistance: props.maxDistance,
            arrowOptions: pairs(props.destinationArrowOptions),
        })));
        body.push(h("div", { key: "cl", style: S.label }, "Sign color"));
        body.push(h("div", { key: "cr", style: S.row },
            pairs(props.colorOptions).map(o => h(Button, { key: o.value, selected: props.color === o.value, onClick: () => routeSet("sign", 0, "color", o.value) }, o.label)),
            h(Button, { selected: props.dividers, onClick: () => routeSet("sign", 0, "dividers", props.dividers ? 0 : 1), style: { marginLeft: "12rem" } }, props.dividers ? "Divider lines: on" : "Divider lines: off")));
    }

    body.push(h("div", { key: "rs", style: merge(S.row, { marginTop: "8rem" }) },
        h(Button, { onClick: () => send("routeReset") }, "Reset to default")));

    const content = h("div", { style: S.body }, body);
    return InfoSection
        ? h(InfoSection, { disableFocus: true }, header, content)
        : h("div", null, header, content);
}

const register = (moduleRegistry) => {
    moduleRegistry.extend(
        "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx",
        "selectedInfoSectionComponents",
        (components) => {
            components[SECTION] = DigitalSignSection;
            components[ROUTE_SECTION] = RouteSignSection;
            return components;
        });
    console.log("USRoadSigns UI module registered.");
};

export default register;
