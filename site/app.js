(() => {
  const config = window.VOCAB_CONFIG || {};
  const version = config.version || "—";
  const size = config.size || "—";
  const sha256 = config.sha256 || "—";

  document.querySelectorAll("[data-version]").forEach((el) => {
    el.textContent = version;
  });

  document.querySelectorAll("[data-size]").forEach((el) => {
    el.textContent = size;
  });

  document.querySelectorAll("[data-sha]").forEach((el) => {
    el.textContent = sha256;
  });

  document.querySelectorAll("[data-updated]").forEach((el) => {
    el.textContent = config.updatedAt ? `更新于 ${config.updatedAt}` : "";
  });

  const downloadLinks = Array.from(document.querySelectorAll("[data-download]"));
  const warning = document.getElementById("download-warning");

  if (config.downloadUrl) {
    downloadLinks.forEach((link) => {
      link.href = config.downloadUrl;
      link.setAttribute("rel", "noopener");
    });
  } else {
    if (warning) {
      warning.hidden = false;
    }

    downloadLinks.forEach((link) => {
      link.classList.add("disabled");
      link.setAttribute("aria-disabled", "true");
      link.addEventListener("click", (event) => event.preventDefault());
    });
  }

  const copyButton = document.getElementById("copy-sha");
  if (copyButton) {
    copyButton.addEventListener("click", async () => {
      try {
        await navigator.clipboard.writeText(sha256);
        copyButton.textContent = "已复制";
        window.setTimeout(() => {
          copyButton.textContent = "复制";
        }, 1600);
      } catch {
        copyButton.textContent = "复制失败";
        window.setTimeout(() => {
          copyButton.textContent = "复制";
        }, 1600);
      }
    });
  }
})();
