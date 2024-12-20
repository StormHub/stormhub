{
    const selectElements = /** @type {HTMLCollectionOf<HTMLSelectElement>} */ (document.getElementsByClassName("nav-on-change"));
    for (const select of selectElements) {
        const change = () => {
            window.location.pathname = select.value;
        };
        select.onchange = change;
    }
}