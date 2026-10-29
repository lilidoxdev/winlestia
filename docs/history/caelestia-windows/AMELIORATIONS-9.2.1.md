# WinLestia / Panneaux 9.2.1 — Correctif des clics à travers le verre

Les auxiliaires natifs du flou étaient transparents visuellement mais leurs rectangles interceptaient les clics lorsqu’ils se trouvaient au-dessus d’une autre application. WS_EX_TRANSPARENT seul ne garantissait pas le passage entre applications ; les auxiliaires utilisent désormais WS_EX_LAYERED avec WS_EX_TRANSPARENT et une alpha globale de 255 pour conserver le rendu du compositeur. Un échec de configuration est contrôlé.

Le test 9.2 avait placé la fenêtre cible devant les auxiliaires, masquant le défaut. Le test 9.2.1 replace explicitement les auxiliaires au premier plan avant les clics physiques. Le défaut est reproduit avec l’ancienne DLL : aucun clic transmis au bureau, à la marge ou à l’angle. Avec la DLL corrigée, les quatre clics (bureau, marge, angle et cadre) atteignent la fenêtre cible indépendante, et le bouton Applications ouvre son espace sans transmettre son clic.

Les 14 contrôles du rendu passent toujours : matière identique sur cadre/dashboard/dock, angles propres et transparence conforme. L’application a été relancée ; les auxiliaires visibles possèdent effectivement les deux styles requis. Les réglages et les fichiers du backend 9.2.0 sont conservés. Les mesures de performances précédentes n’ont pas été répétées pour ce correctif natif.

Rapports : validation-9.3/pointer-after.json, surface-report.json, live-input-report.json. Sauvegarde native : checkpoint-before-9.2.1.
