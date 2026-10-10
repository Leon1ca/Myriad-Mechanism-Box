(() => {
  "use strict";

  const state = {
    paths: {
      declaration: "",
      screenshot: ""
    },
    records: [],
    filter: "all",
    pageSize: 50,
    pendingAction: null
  };

  const elements = {
    directoryDialog: document.querySelector("#directory-dialog"),
    declarationPath: document.querySelector("#declaration-path"),
    screenshotPath: document.querySelector("#screenshot-path"),
    directoryHelper: document.querySelector("#directory-helper"),
    declarationPicker: document.querySelector("#declaration-directory-picker"),
    screenshotPicker: document.querySelector("#screenshot-directory-picker"),
    confirmDialog: document.querySelector("#confirm-dialog"),
    confirmTitle: document.querySelector("#confirm-title"),
    confirmMessage: document.querySelector("#confirm-message"),
    confirmWarning: document.querySelector("#confirm-warning"),
    confirmAction: document.querySelector("#confirm-action"),
    startRecognition: document.querySelector("#start-recognition"),
    toast: document.querySelector("#toast")
  };

  const confirmationContent = {
    declaration: {
      title: "确认清理关单目录？",
      message: "将删除关单读取目录中的全部文件，此操作不可撤销。",
      warning: "请确认当前目录中没有需要保留的关单文件。",
      success: "已确认清理关单目录（原型演示）"
    },
    screenshot: {
      title: "确认清理截图目录？",
      message: "将删除截图保存目录中的全部文件，此操作不可撤销。",
      warning: "请确认当前目录中没有需要保留的核验截图。",
      success: "已确认清理截图目录（原型演示）"
    },
    list: {
      title: "确认清理当前列表？",
      message: "将移除当前已经读取的全部关单记录。",
      warning: "此操作不会删除关单目录中的源文件。",
      success: "当前列表已清理（原型演示）"
    }
  };

  let toastTimer = null;

  function showToast(message) {
    window.clearTimeout(toastTimer);
    elements.toast.textContent = message;
    elements.toast.classList.add("is-visible");
    toastTimer = window.setTimeout(() => {
      elements.toast.classList.remove("is-visible");
    }, 3200);
  }

  function openDirectoryDialog({ requireDeclaration = false } = {}) {
    elements.declarationPath.value = state.paths.declaration;
    elements.screenshotPath.value = state.paths.screenshot;
    elements.directoryHelper.textContent = requireDeclaration
      ? "开始识别前，请先设置关单读取目录。"
      : "路径仅保存在当前设备，可随时重新设置。";
    elements.directoryHelper.classList.toggle("is-error", requireDeclaration);
    elements.directoryDialog.showModal();
  }

  function folderNameFromPicker(input) {
    const firstFile = input.files?.[0];
    if (!firstFile) return "";
    const relativePath = firstFile.webkitRelativePath || firstFile.name;
    return relativePath.split("/")[0] || firstFile.name;
  }

  function openConfirmation(type) {
    const content = confirmationContent[type];
    if (!content) return;
    state.pendingAction = type;
    elements.confirmTitle.textContent = content.title;
    elements.confirmMessage.textContent = content.message;
    elements.confirmWarning.textContent = content.warning;
    elements.confirmDialog.showModal();
  }

  function closeAllSelects(except = null) {
    document.querySelectorAll(".custom-select.is-open").forEach((select) => {
      if (select === except) return;
      select.classList.remove("is-open");
      select.querySelector(".select-trigger")?.setAttribute("aria-expanded", "false");
    });
  }

  function initializeSelect(select) {
    const trigger = select.querySelector(".select-trigger");
    const menu = select.querySelector(".select-menu");
    const options = [...select.querySelectorAll(".select-option")];
    const valueNode = trigger.querySelector("span");

    const open = () => {
      closeAllSelects(select);
      select.classList.add("is-open");
      trigger.setAttribute("aria-expanded", "true");
    };

    const close = ({ restoreFocus = false } = {}) => {
      select.classList.remove("is-open");
      trigger.setAttribute("aria-expanded", "false");
      if (restoreFocus) trigger.focus();
    };

    const selectOption = (option) => {
      options.forEach((item) => {
        const selected = item === option;
        item.classList.toggle("is-selected", selected);
        item.setAttribute("aria-selected", String(selected));
      });
      valueNode.textContent = option.querySelector("span").textContent;

      if (select.dataset.select === "record-filter") {
        state.filter = option.dataset.value;
      } else if (select.dataset.select === "page-size") {
        state.pageSize = Number(option.dataset.value);
      }

      close({ restoreFocus: true });
      renderRecords();
    };

    trigger.addEventListener("click", () => {
      if (select.classList.contains("is-open")) {
        close();
      } else {
        open();
      }
    });

    trigger.addEventListener("keydown", (event) => {
      if (!["ArrowDown", "ArrowUp", "Enter", " "].includes(event.key)) return;
      event.preventDefault();
      open();
      const selectedIndex = Math.max(0, options.findIndex((item) => item.classList.contains("is-selected")));
      options[selectedIndex].focus();
    });

    options.forEach((option) => {
      option.addEventListener("click", () => selectOption(option));
    });

    menu.addEventListener("keydown", (event) => {
      const currentIndex = options.indexOf(document.activeElement);
      if (event.key === "Escape") {
        event.preventDefault();
        close({ restoreFocus: true });
      }
      if (event.key === "ArrowDown") {
        event.preventDefault();
        options[(currentIndex + 1 + options.length) % options.length].focus();
      }
      if (event.key === "ArrowUp") {
        event.preventDefault();
        options[(currentIndex - 1 + options.length) % options.length].focus();
      }
      if (event.key === "Home") {
        event.preventDefault();
        options[0].focus();
      }
      if (event.key === "End") {
        event.preventDefault();
        options.at(-1).focus();
      }
    });
  }

  function renderRecords() {
    const search = document.querySelector("#record-search").value.trim().toLowerCase();
    const filtered = state.records.filter((record) => {
      const matchesSearch = !search || record.number.toLowerCase().includes(search);
      const matchesFilter = state.filter === "all" || record.status === state.filter;
      return matchesSearch && matchesFilter;
    });

    const body = document.querySelector("#records-body");
    body.replaceChildren();

    filtered.slice(0, state.pageSize).forEach((record, index) => {
      const row = document.createElement("tr");
      const values = [
        index + 1,
        record.number,
        record.consignee,
        record.contract,
        record.category,
        record.country,
        record.value,
        record.statusLabel,
        "查看"
      ];
      values.forEach((value) => {
        const cell = document.createElement("td");
        cell.textContent = value;
        row.append(cell);
      });
      body.append(row);
    });

    document.querySelector("#empty-state").hidden = filtered.length > 0;
    document.querySelector("#record-count").textContent = `共 ${filtered.length} 条记录`;
    document.querySelector("#footer-count").textContent = `共 ${filtered.length} 条`;
    document.querySelector("#total-count").textContent = state.records.length;
  }

  document.querySelectorAll(".custom-select").forEach(initializeSelect);

  document.addEventListener("pointerdown", (event) => {
    if (!event.target.closest(".custom-select")) closeAllSelects();
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") closeAllSelects();
  });

  document.querySelector("#record-search").addEventListener("input", renderRecords);

  document.querySelector("#open-directory-settings").addEventListener("click", () => openDirectoryDialog());
  document.querySelector("#choose-declaration-directory").addEventListener("click", () => elements.declarationPicker.click());
  document.querySelector("#choose-screenshot-directory").addEventListener("click", () => elements.screenshotPicker.click());

  elements.declarationPicker.addEventListener("change", () => {
    const folderName = folderNameFromPicker(elements.declarationPicker);
    if (!folderName) return;
    elements.declarationPath.value = folderName;
    elements.directoryHelper.textContent = "关单读取目录已选择，保存后生效。";
    elements.directoryHelper.classList.remove("is-error");
  });

  elements.screenshotPicker.addEventListener("change", () => {
    const folderName = folderNameFromPicker(elements.screenshotPicker);
    if (!folderName) return;
    elements.screenshotPath.value = folderName;
    elements.directoryHelper.textContent = "截图保存目录已选择，保存后生效。";
    elements.directoryHelper.classList.remove("is-error");
  });

  document.querySelector("#save-directory-settings").addEventListener("click", (event) => {
    event.preventDefault();
    state.paths.declaration = elements.declarationPath.value;
    state.paths.screenshot = elements.screenshotPath.value;
    elements.directoryDialog.close("save");
    showToast("目录设置已保存（原型演示）");
  });

  document.querySelector("#clear-declaration-directory").addEventListener("click", () => openConfirmation("declaration"));
  document.querySelector("#clear-screenshot-directory").addEventListener("click", () => openConfirmation("screenshot"));
  document.querySelector("#clear-list").addEventListener("click", () => openConfirmation("list"));

  elements.confirmAction.addEventListener("click", (event) => {
    event.preventDefault();
    const content = confirmationContent[state.pendingAction];
    if (state.pendingAction === "list") {
      state.records = [];
      renderRecords();
    }
    elements.confirmDialog.close("confirm");
    if (content) showToast(content.success);
    state.pendingAction = null;
  });

  elements.startRecognition.addEventListener("click", () => {
    if (!state.paths.declaration) {
      openDirectoryDialog({ requireDeclaration: true });
      return;
    }

    const label = elements.startRecognition.querySelector("span");
    elements.startRecognition.disabled = true;
    label.textContent = "识别中…";
    window.setTimeout(() => {
      elements.startRecognition.disabled = false;
      label.textContent = "开始识别";
      showToast("识别流程已触发（原型演示）");
    }, 900);
  });

  [elements.directoryDialog, elements.confirmDialog].forEach((dialog) => {
    dialog.addEventListener("click", (event) => {
      if (event.target === dialog) dialog.close("cancel");
    });
  });

  renderRecords();
})();
