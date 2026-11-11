// Move, rather than clone, SVGs so marker and label IDs remain unique.
const dialog = document.createElement('dialog');
dialog.className = 'diagram-dialog';
dialog.setAttribute('aria-label', 'Expanded diagram');
const close = document.createElement('button');
close.type = 'button';
close.className = 'diagram-close';
close.textContent = 'Close';
close.setAttribute('aria-label', 'Close expanded diagram');
const viewport = document.createElement('div');
viewport.className = 'diagram-viewport';
viewport.tabIndex = 0;
viewport.setAttribute('role', 'region');
viewport.setAttribute('aria-label', 'Diagram, scroll to explore');
dialog.append(close, viewport);
document.body.append(dialog);
let restore;
close.addEventListener('click', () => dialog.close());
dialog.addEventListener('click', (event) => {
  if (event.target === dialog || event.target === viewport) dialog.close();
});
dialog.addEventListener('close', () => {
  restore?.();
  restore = undefined;
  document.documentElement.classList.remove('diagram-expanded');
});
function enhance() {
  for (const figure of document.querySelectorAll('pre.mermaid[data-processed]')) {
    const svg = figure.querySelector('svg');
    if (!svg || figure.querySelector('.diagram-expand')) continue;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'diagram-expand';
    button.textContent = 'Expand';
    const caption = figure.nextElementSibling?.textContent?.trim();
    button.setAttribute('aria-label', caption?.startsWith('Figure:') ? `Expand diagram. ${caption}` : 'Expand diagram');
    button.addEventListener('click', () => {
      const placeholder = document.createComment('expanded diagram');
      const style = svg.getAttribute('style');
      svg.replaceWith(placeholder);
      const { width, height } = svg.viewBox.baseVal;
      svg.style.width = `${width}px`;
      svg.style.height = `${height}px`;
      svg.style.maxWidth = 'none';
      viewport.append(svg);
      restore = () => {
        placeholder.replaceWith(svg);
        if (style === null) svg.removeAttribute('style');
        else svg.setAttribute('style', style);
        button.focus();
      };
      document.documentElement.classList.add('diagram-expanded');
      dialog.showModal();
      viewport.scrollTo(0, 0);
      close.focus();
    });
    figure.prepend(button);
  }
}
enhance();
new MutationObserver(enhance).observe(document.documentElement, { childList: true, subtree: true });
document.addEventListener('astro:before-swap', () => { if (dialog.open) dialog.close(); });
document.addEventListener('astro:after-swap', () => { document.body.append(dialog); enhance(); });
