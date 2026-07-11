document.addEventListener("click", event => {
    const deviceCell = event.target.closest("td[data-label='Device']");
    if (!deviceCell) {
        return;
    }

    const nameRow = deviceCell.querySelector(".device-name-row");
    const form = deviceCell.querySelector(".rename-form");
    const input = form.querySelector("input");

    if (event.target.closest(".edit-name")) {
        nameRow.hidden = true;
        form.hidden = false;
        input.focus();
        input.select();
    } else if (event.target.closest(".cancel-name")) {
        input.value = input.defaultValue;
        form.hidden = true;
        nameRow.hidden = false;
        nameRow.querySelector(".edit-name").focus();
    }
});

document.addEventListener("keydown", event => {
    if (event.key !== "Escape") {
        return;
    }

    const form = event.target.closest(".rename-form");
    if (!form) {
        return;
    }

    const deviceCell = form.closest("td[data-label='Device']");
    const nameRow = deviceCell.querySelector(".device-name-row");
    const input = form.querySelector("input");

    input.value = input.defaultValue;
    form.hidden = true;
    nameRow.hidden = false;
    nameRow.querySelector(".edit-name").focus();
});
