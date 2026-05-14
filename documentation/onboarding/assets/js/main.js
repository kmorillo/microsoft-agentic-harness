// ============================================================
// Microsoft Agentic Harness — Onboarding Guide
// Sidebar active state, TOC scrollspy, copy buttons
// ============================================================

(function () {
    'use strict';

    // ---------- Active sidebar link ----------
    function setActiveSidebarLink() {
        const path = window.location.pathname.split('/').pop() || 'index.html';
        const links = document.querySelectorAll('.sidebar-link');
        links.forEach((link) => {
            const href = link.getAttribute('href');
            if (!href) return;
            const linkFile = href.split('/').pop();
            if (linkFile === path) {
                link.classList.add('active');
            }
        });
    }

    // ---------- Build TOC from headings ----------
    function buildTOC() {
        const toc = document.querySelector('.toc-list');
        if (!toc) return;
        const headings = document.querySelectorAll('.content h2, .content h3');
        if (headings.length === 0) {
            const tocWrap = document.querySelector('.toc');
            if (tocWrap) tocWrap.style.display = 'none';
            return;
        }
        const frag = document.createDocumentFragment();
        headings.forEach((h, i) => {
            if (!h.id) {
                h.id = h.textContent
                    .toLowerCase()
                    .replace(/[^a-z0-9\s-]/g, '')
                    .trim()
                    .replace(/\s+/g, '-');
            }
            const li = document.createElement('li');
            const a = document.createElement('a');
            a.href = '#' + h.id;
            a.textContent = h.textContent;
            a.dataset.target = h.id;
            if (h.tagName === 'H3') li.classList.add('toc-h3');
            li.appendChild(a);
            frag.appendChild(li);
        });
        toc.appendChild(frag);
    }

    // ---------- TOC scrollspy ----------
    function setupScrollspy() {
        const links = document.querySelectorAll('.toc-list a');
        if (links.length === 0) return;

        const headingMap = new Map();
        links.forEach((link) => {
            const id = link.dataset.target;
            const h = document.getElementById(id);
            if (h) headingMap.set(h, link);
        });

        const observer = new IntersectionObserver(
            (entries) => {
                entries.forEach((entry) => {
                    if (entry.isIntersecting) {
                        links.forEach((l) => l.classList.remove('active'));
                        const link = headingMap.get(entry.target);
                        if (link) link.classList.add('active');
                    }
                });
            },
            {
                rootMargin: '0px 0px -75% 0px',
                threshold: 0,
            }
        );

        headingMap.forEach((_, heading) => observer.observe(heading));
    }

    // ---------- Copy buttons on code blocks ----------
    function addCopyButtons() {
        const blocks = document.querySelectorAll('.code-block');
        blocks.forEach((block) => {
            const header = block.querySelector('.code-block-header');
            const pre = block.querySelector('pre');
            if (!header || !pre) return;
            if (header.querySelector('.copy-btn')) return;

            const btn = document.createElement('button');
            btn.className = 'copy-btn';
            btn.type = 'button';
            btn.textContent = 'Copy';
            btn.addEventListener('click', async () => {
                try {
                    await navigator.clipboard.writeText(pre.innerText);
                    btn.textContent = 'Copied!';
                    btn.classList.add('copied');
                    setTimeout(() => {
                        btn.textContent = 'Copy';
                        btn.classList.remove('copied');
                    }, 1600);
                } catch (e) {
                    btn.textContent = 'Failed';
                }
            });
            header.appendChild(btn);
        });
    }

    // ---------- Init ----------
    document.addEventListener('DOMContentLoaded', () => {
        setActiveSidebarLink();
        buildTOC();
        setupScrollspy();
        addCopyButtons();
    });
})();
