# Social-preview asset specification

Review-only image; do not upload to GitHub before approval.

- Canvas: **1280×640**, PNG, 2:1 aspect ratio.
- Title: **SuperPuTTY Community Edition**.
- Subtitle: **SSH • PowerShell • RDP • VNC • SCP**.
- Badge: **Signed Windows x64 releases**.
- Notice: **Independent community fork**. No official PuTTY or upstream SuperPuTTY logos or endorsement claims.
- Use an existing clean tabbed-workspace screenshot as an inset. Keep surrounding text large and legible at thumbnail size, with generous safe margins and a border that works against both light and dark GitHub surfaces.
- Prefer navy, ivory, and a restrained teal accent; no tracking, external fonts, or stock logos.

Source screenshot: [PowerShell workspace](images/latest-build/powershell.png), an earlier community build with generic session names. The promotional composition must not be presented as a literal screenshot of 1.8.0. The existing screenshot gallery remains the reference for actual application behavior.

The prepared image is [assets/social-preview-1.8.0.png](assets/social-preview-1.8.0.png), inspected at **1280×640**. The built-in image tool produced the composition; a local browser export resized it to the exact GitHub dimensions without changing its content. Review text, screenshot fidelity, and contrast before approval. Generated small UI text is illustrative: use this as a promotional composition, not documentation evidence.

Generation prompt (built-in image tool, review-only compositing): create a 1280×640 navy/ivory GitHub preview with the exact title, subtitle, badge and independent-fork line above; use the real PowerShell workspace as an inset, preserve recognizable tabs and controls, avoid private paths and invented session contents, and do not imply endorsement.

See GitHub's [social-preview guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/customizing-your-repositorys-social-media-preview). The image is not automatically referenced by a deployment workflow or uploaded to repository settings.
