/*
 * US Road Signs - UI module (Cities: Skylines II).
 *
 * Adds the "Message Sign" section to the selected info panel for digital message signs and arrow boards.
 * Plain ES module, no build step: the game exposes React and its cs2/* modules as window globals (the same
 * externals the official mod UI template compiles against).
 *
 * C# counterpart: USRoadSigns.Digital.DigitalSignSection (properties and triggers).
 */

const React = window.React;
const api = window["cs2/api"];
const modding = window["cs2/modding"];

const h = React.createElement;
const GROUP = "USRoadSigns";
const SECTION = "USRoadSigns.Digital.DigitalSignSection";
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
    presetList: { maxHeight: "170rem", overflowY: "scroll", backgroundColor: "rgba(0, 0, 0, 0.25)", borderRadius: "3rem", padding: "2rem" },
    preset: {
        display: "block",
        width: "100%",
        textAlign: "left",
        padding: "4rem 8rem",
        margin: "1rem 0",
        fontFamily: "monospace",
        fontSize: "12rem",
        color: "rgb(255, 178, 60)",
        backgroundColor: "rgba(20, 20, 20, 0.8)",
        border: "none",
        borderRadius: "2rem",
        cursor: "pointer",
    },
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

    // Reload the fields when another sign is selected or C# replaced the message (preset, reset).
    React.useEffect(() => {
        setPages(clonePages(props.pages, lines));
    }, [props.signKey]);

    const commit = (next) => {
        setPages(next);
        send("digitalSetMessage", encode(next));
    };

    const setLine = (p, l, value) => {
        const next = pages.map(page => page.slice());
        next[p][l] = value.toUpperCase().replace(/[\n\f\r]/g, "");
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
                onChange: (e) => setLine(p, l, e.target.value),
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

    if (isText && (props.presets || []).length > 0) {
        body.push(h("div", { key: "pl", style: S.label }, "Common messages"));
        body.push(h("div", { key: "pll", style: S.presetList },
            props.presets.map((label, i) =>
                h("button", { key: "p" + i, style: S.preset, onClick: () => send("digitalApplyPreset", i) }, label))));
    }

    body.push(h("div", { key: "rs", style: merge(S.row, { marginTop: "8rem" }) },
        h(Button, { onClick: () => send("digitalReset") }, "Reset to default")));

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
            return components;
        });
    console.log("USRoadSigns UI module registered.");
};

export default register;
