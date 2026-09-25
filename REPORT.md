# MODERN VILLA XR

**Virtual Open House Experience**

Documentation de présentation - fonctionnalités vérifiées au **24 septembre 2026**, crédits actualisés le **25 septembre 2026**

![Piscine et terrasse de la villa, rendu de la scène Unity](../Evidence/final-polish/after/pool-terrace.png)

*Image : vue fixe capturée dans Unity après polissage visuel. Elle ne constitue pas une capture ni une mesure sur Quest 3.*

## 1. Vue d'ensemble du projet

**Modern Villa XR** adapte le thème « Virtual Tour & Virtual Open House » du brief *Journey Through XR* en une visite immobilière virtuelle. L'utilisateur explore à distance une propriété complète, librement ou selon un parcours guidé. La cible est le **Meta Quest 3 autonome** ; le projet utilise **Unity 6000.6.0f1 (Unity 6)**, **OpenXR** et **XR Interaction Toolkit 3.6.0**. Le menu principal et la villa sont les deux scènes actives de la configuration de build. [Configuration Unity](../ProjectSettings/EditorBuildSettings.asset) · [Packages](../Packages/manifest.json)


## 2. Besoin utilisateur

Photos, vidéos et plans montrent une annonce immobilière sans restituer aussi directement les distances, les circulations et les proportions ressenties lors d'une visite sur place. Modern Villa XR propose une exploration spatiale immersive, un passage rapide d'une pièce à l'autre et des informations contextuelles. La visite guidée aide à découvrir les six espaces principaux sans imposer au visiteur de trouver lui-même l'itinéraire.

## 3. Expérience utilisateur et storyboard

```text
Lancement → Menu principal
             ├─ Visite libre → Exploration → POI / menu des pièces / interactions
             └─ Visite guidée → Cuisine → Salon → Salle à manger
                                  → Chambre principale → Salle de bains
                                  → Piscine / terrasse → Fin de visite
```

En visite libre, l'utilisateur se déplace dans la villa et les extérieurs accessibles, consulte les six points d'intérêt (POI), puis peut ouvrir le menu des pièces pour se téléporter directement. En visite guidée, la progression suit l'ordre indiqué ; **Previous**, **Next**, **Finish**, **Exit**, **Restart Tour** et **Free Exploration** permettent de revenir, conclure, quitter, recommencer ou poursuivre librement. Les changements directs de pièce utilisent un fondu au noir plutôt qu'un vol automatique de la caméra. [Visite guidée](../Evidence/modern-villa-guided-tour.md) · [Téléportation](../Evidence/modern-villa-room-teleportation.md)

## 4. Fonctionnalités principales

- **Navigation XR :** déplacement continu ou téléportation selon le choix de confort, rotation par paliers, téléportation directe entre six pièces, escalier praticable et itinéraires intérieurs/extérieurs. Les passages vers la terrasse, le jardin, les abords de la piscine et le pavillon disposent de surfaces et de protections de collision simplifiées. [Audit des corrections de navigation](../Evidence/navigation-observation-fix/navigation-audit.md)
- **Six POI :** Kitchen, Living Room, Dining Room, Master Bedroom, Bathroom et Pool / Terrace. Chaque marqueur ouvre un panneau avec le nom et une description de l'espace ; le rayon du contrôleur et la gâchette activent les boutons. [Validation des POI](../Evidence/modern-villa-poi-pass.md)
- **Menu XR des pièces :** ouverture avec le bouton Menu du contrôleur gauche ; sélection et fermeture par rayon XR et gâchette. Une sélection déclenche la téléportation vers l'ancre correspondante, avec fondu. [Menu](../Assets/CampusExplorer/Runtime/XRRoomMenu.cs) · [Téléporteur](../Assets/CampusExplorer/Runtime/XRRoomTeleporter.cs)
- **Environnement interactif :** trois interrupteurs commandent des éclairages de la cuisine, du salon et de la chambre principale ; douze portes battantes et deux paires de portes vitrées coulissantes s'ouvrent et se ferment. **Douze accessoires** sont saisissables par la poignée : huit coussins/oreillers, trois flacons ou distributeurs et un livre. Ces comportements ont été contrôlés en Play Mode ; leur maniement sur le dernier APK Quest reste à vérifier. [Lumières](../Evidence/interactive-light-switches.md) · [Portes](../Evidence/interactive-doors-report.md) · [Accessoires](../Evidence/grabbable-props/apply-report.txt)

| Cuisine | Salon |
| --- | --- |
| ![Cuisine, rendu de la scène Unity](../Evidence/final-polish/after/kitchen.png) | ![Salon, rendu de la scène Unity](../Evidence/final-polish/after/living-room.png) |

*Captures Unity des pièces ; aucun panneau POI n'est ouvert.*

![Salle à manger, rendu de la scène Unity](../Evidence/final-polish/after/dining-room.png)

*La salle à manger communique visuellement avec la terrasse ; capture Unity à cadrage fixe.*

## 5. Menu principal

La première scène propose **Start Free Tour**, **Guided Tour**, **Controls**, **Comfort**, **Credits** et **Quit**. Le panneau Controls explique les commandes ; Comfort permet de choisir une rotation de **30° ou 45°** et un déplacement **continu ou par téléportation**. La configuration initiale est 45° et déplacement continu. Le panneau Credits des sources du projet nomme désormais **acrts076** et la licence du modèle. L'APK actuel est antérieur à cette correction : l'écran de crédits mis à jour n'y a pas été vérifié **[NOT VERIFIED]**. L'auteur du projet et certaines attributions de textures restent à compléter. [Scène du menu](../Assets/CampusExplorer/Scenes/MainMenu.unity) · [Gestionnaire](../Assets/CampusExplorer/Runtime/MainMenuManager.cs) · [Tests du menu](../Evidence/main-menu-controls-tests.xml)

![Menu principal de Modern Villa XR, capture Unity](../Evidence/main-menu-preview.png)

*Capture Unity du menu principal. Lisibilité et visée au casque : [NOT VERIFIED].*

## 6. Architecture technique

Le socle associe **Unity 6000.6.0f1**, **OpenXR 1.18.0** et **XR Interaction Toolkit 3.6.0**. La scène de lancement transmet le choix de visite et les paramètres de confort à la scène de la villa. Dans celle-ci, **XR Origin** et **XR Interaction Manager** assurent le suivi et les interactions ; **XRRoomMenu** et **XRRoomTeleporter** gèrent les destinations, **XRGuidedTour** orchestre les étapes, les composants **PointOfInterest / InfoPanel** affichent les descriptions et des composants dédiés pilotent portes, lumières et objets saisissables. Les volumes de collision invisibles sont séparés du modèle visuel. [Scripts du projet](../Assets/CampusExplorer/Runtime) · [Réception du lancement](../Assets/CampusExplorer/Scripts/MainMenuLaunchReceiver.cs)

```text
MainMenu → choix visite + confort → ModernVillaTour
                                   ├─ XR Origin / XRI / OpenXR
                                   ├─ Menu pièces → téléporteur → ancres + fondu
                                   ├─ Visite guidée → téléporteur + panneau
                                   ├─ POI → panneaux d'information
                                   └─ Portes / lumières / accessoires / collisions
```

La cible Android est **ARM64 avec Vulkan**. L'APK actuellement présent est signé ; aucune mesure de fluidité sur le Quest 3 autonome n'est documentée. [Projet Unity](../ProjectSettings/ProjectVersion.txt) · [Configuration de build](../Assets/CampusExplorer/Editor/Quest3BuildTools.cs)

## 7. Stratégie de collision et de navigation

Le paquet 3D d'origine ne fournit pas de collisions Unity exploitables : ni objets de collision identifiés dans les sources, ni colliders dans les scènes d'import. La navigation utilise donc des **BoxColliders simplifiés**, sans MeshCollider détaillé. Une rampe invisible lisse la montée de l'escalier ; des rampes et surfaces de transition soutiennent la terrasse, le jardin et l'accès au pavillon. Cette géométrie rend la capsule du joueur et les destinations de téléportation plus prévisibles, tout en évitant le coût de collisions sur chaque détail du modèle. [Audit de la source](../Evidence/modern-villa-final-collision-audit/FINAL_COLLISION_AUDIT.md) · [Parcours extérieur](../Evidence/modern-villa-exterior-collision/exterior-walkable-audit.md)

**État actuel de la scène : 178 BoxColliders sérialisés, 0 MeshCollider.** Ils se répartissent en **150** volumes d'environnement après les corrections du 23 septembre, **16** volumes mobiles de portes et **12** volumes ajoutés aux accessoires saisissables. Le chiffre antérieur de 142 colliders décrit une version dépassée. Les nouveaux passages ont réussi leurs vérifications ciblées en Play Mode ; le ressenti de l'ensemble du parcours sur le dernier APK reste . [Corrections observées sous Quest Link](../Evidence/navigation-observation-fix/navigation-audit.md) · [Objets saisissables](../Evidence/grabbable-props/apply-report.txt)

![Pavillon et extérieur, rendu de la scène Unity](../Evidence/final-polish/after/garden-pavilion.png)

*Vue du pavillon et des extérieurs depuis une caméra Unity. Parcours physique sur le dernier APK : [NOT VERIFIED].*

## 8. Intégration visuelle

La scène reprend les géométries, matériaux et textures du paquet villa. Le travail visuel a porté sur l'espace colorimétrique **Linear**, la reconnexion du **HDRI d'origine du paquet**, les affectations de matériaux, le verre, les masques alpha du feuillage et la teinte de l'herbe. Des lumières temps réel plus mesurées remplacent des lumières importées inadaptées au rendu Unity. L'objectif est de conserver l'intention visuelle de la villa tout en préparant un rendu compatible avec la cible Quest ; l'équivalence avec les rendus hors ligne et le coût GPU ne sont pas démontrés. [Stabilisation visuelle](../Evidence/visual-stabilization/visual-stabilization-report.md) · [Polissage final](../Evidence/final-polish/final-polish-report.md)

| Avant - cuisine | Après - cuisine |
| --- | --- |
| ![Cuisine avant correction du feuillage](../Evidence/final-polish/before/kitchen.png) | ![Cuisine après correction du feuillage](../Evidence/final-polish/after/kitchen.png) |

*Captures Unity à cadrage fixe ; la différence la plus visible concerne le feuillage au-delà de la fenêtre. [Planche comparative complète](../Evidence/final-polish/before-after-contact-sheet.jpg).*

## 9. Confort VR

La téléportation de surface et la téléportation directe des pièces offrent une alternative au déplacement continu. Le menu permet la rotation par paliers de 30° ou 45°. Le fondu masque les sauts entre pièces ; le parcours guidé réutilise les mêmes ancres sans déplacer la caméra dans l'espace par animation. Les six arrivées ont passé un précontrôle Unity de surface et de volume libre. Leur confort réel, la hauteur perçue et la facilité de visée des panneaux sur le dernier APK restent **[NOT VERIFIED]**. [Précontrôle des placements](../Evidence/final-polish/placement-audit.txt) · [Checklist Quest](../Evidence/final-polish/quest3-checklist.md)

## 10. Tests et validation

**Validation automatisée.** L'import et la compilation Unity de la passe finale du 23 septembre ont réussi ; les tests Play Mode de la villa ont donné **5/5**. Après les corrections de navigation observées sous Quest Link, les tests ciblés ont donné **4/4** et la régression villa **5/5**. Le 24 septembre, les tests ciblés des accessoires donnent **2/2**, ceux du menu et du confort **3/3**. Une exécution globale de **14 tests donne 11 réussites et 3 échecs** : les trois scénarios historiques CampusTour, NotreDameTour et IncrementA ne figurent plus dans les scènes de build actives. Cette anomalie de la suite globale reste à traiter ; elle n'est pas présentée comme une réussite générale. [Résultats villa](../Evidence/grabbable-props/tour-playmode-results.xml) · [Résultats accessoires](../Evidence/grabbable-props/playmode-results.xml) · [Résultats menu](../Evidence/main-menu-controls-tests.xml) · [Suite globale](../Evidence/grabbable-props/full-playmode-results.xml)

**Build Android.** Un APK récent est présent sous `Builds/Quest3/` ; son manifeste et sa signature ont été inspectés (ARM64, Vulkan, capacités VR et signature valide). Le dernier journal Unity conservé signale un résultat de build réussi, mais son récapitulatif personnalisé contient un décompte d'erreurs contradictoire et ne correspond pas en taille à l'APK le plus récent. La traçabilité exacte de cet APK final est donc **[NOT VERIFIED]**. [Journal Unity](../Evidence/grabbable-props/android-build-final-unity.log) · [APK](../Builds/Quest3/CampusExplorerXR-Quest3.apk)

**Validation physique Quest.** Des observations ont été faites avec **Quest Link en mode PC VR**, notamment pour la navigation le 23 septembre. Elles ont conduit à corriger des zones sans appui et un garde-corps. Aucun parcours complet documenté du **dernier APK en mode autonome Quest 3**, ni mesure de fréquence d'images ou de temps GPU, n'a été trouvé : **[NOT VERIFIED]**. [Audit après observation](../Evidence/navigation-observation-fix/navigation-audit.md) · [Contrôles physiques encore ouverts](../Evidence/final-polish/quest3-checklist.md)

## 11. Limites connues

- Le contrôle autonome du dernier APK reste à réaliser : lisibilité des menus/POI, maniement des portes et accessoires, confort des arrivées, vitrages et fluidité sont **[NOT VERIFIED]**.
- Des captures propres des POI ouverts, du menu des pièces, de la visite guidée et des interactions vues dans le casque restent. Les images de ce rapport sont des captures Unity.
- Certaines surfaces ou affectations du paquet source restent à examiner, notamment des éléments signalés `Default-Material` ; une bande claire est visible près de la jonction terrasse/pelouse. Les rendus Unity temps réel diffèrent des références hors ligne. [Backlog actuel](../POLISH_BACKLOG.md)
- L'APK actuel est volumineux (environ **1,32 Go** sur disque) ; profilage et réduction de taille restent à faire. [Artefact Android](../Builds/Quest3/CampusExplorerXR-Quest3.apk)
- La fiche et la licence du modèle villa sont identifiées, mais certaines textures du paquet et l'auteur du projet demandent encore attribution ou vérification. Le nouvel écran Credits n'a pas été validé dans l'APK actuel **[NOT VERIFIED]**. La visualisation de collision F8 est disponible en éditeur et désactivée par défaut. [Menu Credits](../Assets/CampusExplorer/Editor/MainMenuSceneBuilder.cs) · [Rapport final](../Evidence/final-polish/final-polish-report.md)

## 12. Future Improvements

Pistes **non intégrées ou non validées** : narration et audio spatial, informations immobilières plus détaillées, préférences conservées entre sessions, davantage d'accessoires manipulables, reflets dynamiques améliorés et optimisation mesurée sur Quest 3. La narration audio des pièces n'est pas intégrée dans la scène actuelle.

## 13. Assets et crédits

- **Modern Luxury Villa Furnished and Animated Blender 5 - modèle, mobilier et textures du paquet** : créateur indiqué **acrts076**, [fiche CGTrader du modèle #6876788](https://www.cgtrader.com/free-3d-models/architectural/other/modern-luxury-villa-furnished-and-animated-blender-5), licence affichée **Royalty Free License (no AI)**. Le fichier local `Modern_Residence_Interior.fbx` et l'audit de l'archive correspondent très fortement à cette fiche : **322 770 sommets, 307 823 faces et 593 780 triangles**. Selon les [conditions CGTrader](https://www.cgtrader.com/pages/terms-and-conditions), la licence permet d'incorporer le modèle dans une application VR et de créer ses propres rendus promotionnels, sous réserve notamment d'empêcher raisonnablement l'extraction des fichiers du modèle. Elle ne permet pas de redistribuer le modèle brut. La mention « no AI » interdit l'usage du modèle pour entraîner une IA ; elle ne garantit pas l'absence d'images générées dans les textures livrées. L'archive ne contient pas de notice de licence séparée ; les droits de certains éléments tiers intégrés restent **[NOT VERIFIED]**. [Audit du paquet](../Evidence/modern-villa-audit/source-audit.json) · [Source intégrée](../Assets/CampusExplorer/External/ModernLuxuryVilla)
- **Horn-koppe Spring (HDRI)** : Grzegorz Wronkowski, [Poly Haven](https://polyhaven.com/a/horn-koppe_spring), **CC0**. Le fichier est fourni dans le paquet villa et référencé par le ciel de la scène. [Skybox Unity](../Assets/CampusExplorer/Lighting/ModernVillaReferenceSky.mat)
- **Asphalt 07 (textures)** : Charlotte Baglioni, [Poly Haven](https://polyhaven.com/a/asphalt_07), **CC0**. Famille de fichiers utilisée par `Wall_Paint_White_01` ; identité exacte des fichiers du paquet avec l'original publié : **[NOT VERIFIED]**.
- **Book Pattern (textures)** : Rob Tuytel, [Poly Haven](https://polyhaven.com/a/book_pattern), **CC0**. Famille utilisée par le matériau du livre ; identité exacte : **[NOT VERIFIED]**.
- **Asphalt 031 (textures)** : ambientCG, [fiche source](https://ambientcg.com/view?id=Asphalt031), **CC0**. Famille utilisée par `Asphalt_Street` ; identité exacte : **[NOT VERIFIED]**.
- **Autres textures du paquet villa** : des fichiers portant des noms de captures ou photographies externes et **14 images nommées `Gemini_Generated_Image_*`** sont affectés à des matériaux de la scène. Auteurs, sources initiales et droits de ces éléments. Leur présence interdit de reprendre sans vérification l'ancienne mention « aucun contenu généré par IA » des documents historiques.
- **Outils et ressources Unity** : Unity Technologies fournit le moteur, OpenXR/XR Interaction Toolkit et la police d'interface intégrée. Aucun clip de narration ou autre son n'est intégré à la scène villa actuelle. Un son de démonstration XRI existe dans le projet, sans référence dans cette scène. [Packages](../Packages/manifest.json)

Les liens des quatre ressources CC0 renvoient à leurs fiches officielles. La licence **Royalty Free License (no AI)** s'applique au modèle de la fiche CGTrader ; elle ne remplace pas la vérification des droits éventuels sur des contenus tiers visibles dans certaines textures.

## 14. Vidéo promotionnelle

Le livrable vidéo prévu doit présenter le besoin utilisateur, l'exploration libre, les POI, la téléportation entre pièces, la visite guidée et les interactions. **Lien de la vidéo finale du projet. Aucun lien de vidéo promotionnelle finale n'a été établi dans le dépôt.

La [fiche CGTrader](https://www.cgtrader.com/free-3d-models/architectural/other/modern-luxury-villa-furnished-and-animated-blender-5) montre aussi une vidéo d'aperçu du vendeur. Cette vidéo n'est pas dans l'archive source du projet. La licence du modèle autorise des **captures ou rendus vidéo créés pour Modern Villa XR** à partir du modèle incorporé ; elle ne donne pas, à elle seule, le droit de télécharger, monter ou republier la vidéo d'aperçu du vendeur. Avant de l'intégrer à la promotion, obtenir une **autorisation écrite d'acrts076** couvrant les supports de diffusion, les modifications, le crédit et les éventuels droits musicaux. [Conditions CGTrader](https://www.cgtrader.com/pages/terms-and-conditions)

## 15. Conclusion

Modern Villa XR réunit dans un même prototype de **Virtual Open House** l'exploration immersive, une visite guidée en six étapes, des informations contextuelles, des interactions environnementales et un déploiement Android destiné au Meta Quest 3. Le fonctionnement a été vérifié dans Unity ; la démonstration complète sur le dernier APK autonome, le profilage et certaines attributions de textures restent à finaliser avant la remise définitive.
