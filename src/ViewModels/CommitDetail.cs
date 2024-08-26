﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public partial class CommitDetail : ObservableObject
    {
        public DiffContext DiffContext
        {
            get => _diffContext;
            private set => SetProperty(ref _diffContext, value);
        }

        public int ActivePageIndex
        {
            get => _activePageIndex;
            set => SetProperty(ref _activePageIndex, value);
        }

        public Models.Commit Commit
        {
            get => _commit;
            set
            {
                if (SetProperty(ref _commit, value))
                    Refresh();
            }
        }

        public string FullMessage
        {
            get => _fullMessage;
            private set => SetProperty(ref _fullMessage, value);
        }

        public List<Models.Change> Changes
        {
            get => _changes;
            set => SetProperty(ref _changes, value);
        }

        public List<Models.Change> VisibleChanges
        {
            get => _visibleChanges;
            set => SetProperty(ref _visibleChanges, value);
        }

        public List<Models.Change> SelectedChanges
        {
            get => _selectedChanges;
            set
            {
                if (SetProperty(ref _selectedChanges, value))
                {
                    if (value == null || value.Count != 1)
                        DiffContext = null;
                    else
                        DiffContext = new DiffContext(_repo.FullPath, new Models.DiffOption(_commit, value[0]), _diffContext);
                }
            }
        }

        public string SearchChangeFilter
        {
            get => _searchChangeFilter;
            set
            {
                if (SetProperty(ref _searchChangeFilter, value))
                {
                    RefreshVisibleChanges();
                }
            }
        }

        public object ViewRevisionFileContent
        {
            get => _viewRevisionFileContent;
            set => SetProperty(ref _viewRevisionFileContent, value);
        }

        public AvaloniaList<Models.CommitLink> WebLinks
        {
            get;
            private set;
        } = new AvaloniaList<Models.CommitLink>();

        public AvaloniaList<Models.IssueTrackerRule> IssueTrackerRules
        {
            get => _repo.Settings?.IssueTrackerRules;
        }

        public CommitDetail(Repository repo)
        {
            _repo = repo;

            foreach (var remote in repo.Remotes)
            {
                if (remote.TryGetVisitURL(out var url))
                {
                    if (url.StartsWith("https://github.com/", StringComparison.Ordinal))
                        WebLinks.Add(new Models.CommitLink() { Name = "Github", URLPrefix = $"{url}/commit/" });
                    else if (url.StartsWith("https://gitlab.com/", StringComparison.Ordinal))
                        WebLinks.Add(new Models.CommitLink() { Name = "GitLab", URLPrefix = $"{url}/-/commit/" });
                    else if (url.StartsWith("https://gitee.com/", StringComparison.Ordinal))
                        WebLinks.Add(new Models.CommitLink() { Name = "Gitee", URLPrefix = $"{url}/commit/" });
                    else if (url.StartsWith("https://bitbucket.org/", StringComparison.Ordinal))
                        WebLinks.Add(new Models.CommitLink() { Name = "Bitbucket", URLPrefix = $"{url}/commits/" });
                }
            }
        }

        public void Cleanup()
        {
            _repo = null;
            _commit = null;
            if (_changes != null)
                _changes.Clear();
            if (_visibleChanges != null)
                _visibleChanges.Clear();
            if (_selectedChanges != null)
                _selectedChanges.Clear();
            _searchChangeFilter = null;
            _diffContext = null;
            _viewRevisionFileContent = null;
            _cancelToken = null;
        }

        public void NavigateTo(string commitSHA)
        {
            _repo?.NavigateToCommit(commitSHA);
        }

        public List<Models.Decorator> GetRefsContainsThisCommit()
        {
            return new Commands.QueryRefsContainsCommit(_repo.FullPath, _commit.SHA).Result();
        }

        public void ClearSearchChangeFilter()
        {
            SearchChangeFilter = string.Empty;
        }

        public List<Models.Object> GetRevisionFilesUnderFolder(string parentFolder)
        {
            return new Commands.QueryRevisionObjects(_repo.FullPath, _commit.SHA, parentFolder).Result();
        }

        public void ViewRevisionFile(Models.Object file)
        {
            if (file == null)
            {
                ViewRevisionFileContent = null;
                return;
            }

            switch (file.Type)
            {
                case Models.ObjectType.Blob:
                    Task.Run(() =>
                    {
                        var isBinary = new Commands.IsBinary(_repo.FullPath, _commit.SHA, file.Path).Result();
                        if (isBinary)
                        {
                            var ext = Path.GetExtension(file.Path);
                            if (IMG_EXTS.Contains(ext))
                            {
                                var stream = Commands.QueryFileContent.Run(_repo.FullPath, _commit.SHA, file.Path);
                                var bitmap = stream.Length > 0 ? new Bitmap(stream) : null;
                                Dispatcher.UIThread.Invoke(() =>
                                {
                                    ViewRevisionFileContent = new Models.RevisionImageFile() { Image = bitmap };
                                });
                            }
                            else
                            {
                                var size = new Commands.QueryFileSize(_repo.FullPath, file.Path, _commit.SHA).Result();
                                Dispatcher.UIThread.Invoke(() =>
                                {
                                    ViewRevisionFileContent = new Models.RevisionBinaryFile() { Size = size };
                                });
                            }

                            return;
                        }

                        var contentStream = Commands.QueryFileContent.Run(_repo.FullPath, _commit.SHA, file.Path);
                        var content = new StreamReader(contentStream).ReadToEnd();
                        var matchLFS = REG_LFS_FORMAT().Match(content);
                        if (matchLFS.Success)
                        {
                            var obj = new Models.RevisionLFSObject() { Object = new Models.LFSObject() };
                            obj.Object.Oid = matchLFS.Groups[1].Value;
                            obj.Object.Size = long.Parse(matchLFS.Groups[2].Value);

                            Dispatcher.UIThread.Invoke(() => ViewRevisionFileContent = obj);
                        }
                        else
                        {
                            var txt = new Models.RevisionTextFile() { FileName = file.Path, Content = content };
                            Dispatcher.UIThread.Invoke(() => ViewRevisionFileContent = txt);
                        }
                    });
                    break;
                case Models.ObjectType.Commit:
                    Task.Run(() =>
                    {
                        var submoduleRoot = Path.Combine(_repo.FullPath, file.Path);
                        var commit = new Commands.QuerySingleCommit(submoduleRoot, file.SHA).Result();
                        if (commit != null)
                        {
                            var body = new Commands.QueryCommitFullMessage(submoduleRoot, file.SHA).Result();
                            Dispatcher.UIThread.Invoke(() =>
                            {
                                ViewRevisionFileContent = new Models.RevisionSubmodule()
                                {
                                    Commit = commit,
                                    FullMessage = body,
                                };
                            });
                        }
                        else
                        {
                            Dispatcher.UIThread.Invoke(() =>
                            {
                                ViewRevisionFileContent = new Models.RevisionSubmodule()
                                {
                                    Commit = new Models.Commit() { SHA = file.SHA },
                                    FullMessage = string.Empty,
                                };
                            });
                        }
                    });
                    break;
                default:
                    ViewRevisionFileContent = null;
                    break;
            }
        }

        public ContextMenu CreateChangeContextMenu(Models.Change change)
        {
            var menu = new ContextMenu();

            var diffWithMerger = new MenuItem();
            diffWithMerger.Header = App.Text("DiffWithMerger");
            diffWithMerger.Icon = App.CreateMenuIcon("Icons.OpenWith");
            diffWithMerger.Click += (_, ev) =>
            {
                var toolType = Preference.Instance.ExternalMergeToolType;
                var toolPath = Preference.Instance.ExternalMergeToolPath;
                var opt = new Models.DiffOption(_commit, change);

                Task.Run(() => Commands.MergeTool.OpenForDiff(_repo.FullPath, toolType, toolPath, opt));
                ev.Handled = true;
            };
            menu.Items.Add(diffWithMerger);
            menu.Items.Add(new MenuItem { Header = "-" });

            var fullPath = Path.Combine(_repo.FullPath, change.Path);
            if (File.Exists(fullPath))
            {
                var resetToThisRevision = new MenuItem();
                resetToThisRevision.Header = App.Text("ChangeCM.CheckoutThisRevision");
                resetToThisRevision.Icon = App.CreateMenuIcon("Icons.File.Checkout");
                resetToThisRevision.Click += (_, ev) =>
                {
                    new Commands.Checkout(_repo.FullPath).FileWithRevision(change.Path, $"{_commit.SHA}");
                    ev.Handled = true;
                };

                var resetToFirstParent = new MenuItem();
                resetToFirstParent.Header = App.Text("ChangeCM.CheckoutFirstParentRevision");
                resetToFirstParent.Icon = App.CreateMenuIcon("Icons.File.Checkout");
                resetToFirstParent.IsEnabled = _commit.Parents.Count > 0 && change.Index != Models.ChangeState.Added && change.Index != Models.ChangeState.Renamed;
                resetToFirstParent.Click += (_, ev) =>
                {
                    new Commands.Checkout(_repo.FullPath).FileWithRevision(change.Path, $"{_commit.SHA}~1");
                    ev.Handled = true;
                };

                var explore = new MenuItem();
                explore.Header = App.Text("RevealFile");
                explore.Icon = App.CreateMenuIcon("Icons.Explore");
                explore.Click += (_, ev) =>
                {
                    Native.OS.OpenInFileManager(fullPath, true);
                    ev.Handled = true;
                };

                menu.Items.Add(resetToThisRevision);
                menu.Items.Add(resetToFirstParent);
                menu.Items.Add(new MenuItem { Header = "-" });
                menu.Items.Add(explore);
                menu.Items.Add(new MenuItem { Header = "-" });
            }

            if (change.Index != Models.ChangeState.Deleted)
            {
                var history = new MenuItem();
                history.Header = App.Text("FileHistory");
                history.Icon = App.CreateMenuIcon("Icons.Histories");
                history.Click += (_, ev) =>
                {
                    var window = new Views.FileHistories() { DataContext = new FileHistories(_repo, change.Path) };
                    window.Show();
                    ev.Handled = true;
                };

                var blame = new MenuItem();
                blame.Header = App.Text("Blame");
                blame.Icon = App.CreateMenuIcon("Icons.Blame");
                blame.Click += (_, ev) =>
                {
                    var window = new Views.Blame() { DataContext = new Blame(_repo.FullPath, change.Path, _commit.SHA) };
                    window.Show();
                    ev.Handled = true;
                };

                menu.Items.Add(history);
                menu.Items.Add(blame);
                menu.Items.Add(new MenuItem { Header = "-" });
            }

            var copyPath = new MenuItem();
            copyPath.Header = App.Text("CopyPath");
            copyPath.Icon = App.CreateMenuIcon("Icons.Copy");
            copyPath.Click += (_, ev) =>
            {
                App.CopyText(change.Path);
                ev.Handled = true;
            };
            menu.Items.Add(copyPath);

            var copyFileName = new MenuItem();
            copyFileName.Header = App.Text("CopyFileName");
            copyFileName.Icon = App.CreateMenuIcon("Icons.Copy");
            copyFileName.Click += (_, e) =>
            {
                App.CopyText(Path.GetFileName(change.Path));
                e.Handled = true;
            };
            menu.Items.Add(copyFileName);

            return menu;
        }

        public ContextMenu CreateRevisionFileContextMenu(Models.Object file)
        {
            var fullPath = Path.Combine(_repo.FullPath, file.Path);

            var resetToThisRevision = new MenuItem();
            resetToThisRevision.Header = App.Text("ChangeCM.CheckoutThisRevision");
            resetToThisRevision.Icon = App.CreateMenuIcon("Icons.File.Checkout");
            resetToThisRevision.IsEnabled = File.Exists(fullPath);
            resetToThisRevision.Click += (_, ev) =>
            {
                new Commands.Checkout(_repo.FullPath).FileWithRevision(file.Path, $"{_commit.SHA}");
                ev.Handled = true;
            };

            var explore = new MenuItem();
            explore.Header = App.Text("RevealFile");
            explore.Icon = App.CreateMenuIcon("Icons.Explore");
            explore.IsEnabled = File.Exists(fullPath);
            explore.Click += (_, ev) =>
            {
                Native.OS.OpenInFileManager(fullPath, file.Type == Models.ObjectType.Blob);
                ev.Handled = true;
            };

            var saveAs = new MenuItem();
            saveAs.Header = App.Text("SaveAs");
            saveAs.Icon = App.CreateMenuIcon("Icons.Save");
            saveAs.IsEnabled = file.Type == Models.ObjectType.Blob;
            saveAs.Click += async (_, ev) =>
            {
                var storageProvider = App.GetStorageProvider();
                if (storageProvider == null)
                    return;

                var options = new FolderPickerOpenOptions() { AllowMultiple = false };
                var selected = await storageProvider.OpenFolderPickerAsync(options);
                if (selected.Count == 1)
                {
                    var saveTo = Path.Combine(selected[0].Path.LocalPath, Path.GetFileName(file.Path));
                    Commands.SaveRevisionFile.Run(_repo.FullPath, _commit.SHA, file.Path, saveTo);
                }

                ev.Handled = true;
            };

            var history = new MenuItem();
            history.Header = App.Text("FileHistory");
            history.Icon = App.CreateMenuIcon("Icons.Histories");
            history.Click += (_, ev) =>
            {
                var window = new Views.FileHistories() { DataContext = new FileHistories(_repo, file.Path) };
                window.Show();
                ev.Handled = true;
            };

            var blame = new MenuItem();
            blame.Header = App.Text("Blame");
            blame.Icon = App.CreateMenuIcon("Icons.Blame");
            blame.IsEnabled = file.Type == Models.ObjectType.Blob;
            blame.Click += (_, ev) =>
            {
                var window = new Views.Blame() { DataContext = new Blame(_repo.FullPath, file.Path, _commit.SHA) };
                window.Show();
                ev.Handled = true;
            };

            var copyPath = new MenuItem();
            copyPath.Header = App.Text("CopyPath");
            copyPath.Icon = App.CreateMenuIcon("Icons.Copy");
            copyPath.Click += (_, ev) =>
            {
                App.CopyText(file.Path);
                ev.Handled = true;
            };

            var copyFileName = new MenuItem();
            copyFileName.Header = App.Text("CopyFileName");
            copyFileName.Icon = App.CreateMenuIcon("Icons.Copy");
            copyFileName.Click += (_, e) =>
            {
                App.CopyText(Path.GetFileName(file.Path));
                e.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(resetToThisRevision);
            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(explore);
            menu.Items.Add(saveAs);
            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(history);
            menu.Items.Add(blame);
            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(copyPath);
            menu.Items.Add(copyFileName);
            return menu;
        }

        private void Refresh()
        {
            _changes = null;
            FullMessage = string.Empty;
            Changes = [];
            VisibleChanges = null;
            SelectedChanges = null;
            ViewRevisionFileContent = null;

            if (_commit == null)
                return;

            Task.Run(() =>
            {
                var fullMessage = new Commands.QueryCommitFullMessage(_repo.FullPath, _commit.SHA).Result();
                Dispatcher.UIThread.Invoke(() => FullMessage = fullMessage);
            });

            if (_cancelToken != null)
                _cancelToken.Requested = true;

            _cancelToken = new Commands.Command.CancelToken();
            Task.Run(() =>
            {
                var parent = _commit.Parents.Count == 0 ? "4b825dc642cb6eb9a060e54bf8d69288fbee4904" : _commit.Parents[0];
                var cmdChanges = new Commands.CompareRevisions(_repo.FullPath, parent, _commit.SHA) { Cancel = _cancelToken };
                var changes = cmdChanges.Result();
                var visible = changes;
                if (!string.IsNullOrWhiteSpace(_searchChangeFilter))
                {
                    visible = new List<Models.Change>();
                    foreach (var c in changes)
                    {
                        if (c.Path.Contains(_searchChangeFilter, StringComparison.OrdinalIgnoreCase))
                            visible.Add(c);
                    }
                }

                if (!cmdChanges.Cancel.Requested)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Changes = changes;
                        VisibleChanges = visible;
                    });
                }
            });
        }

        private void RefreshVisibleChanges()
        {
            if (_changes == null)
                return;

            if (string.IsNullOrEmpty(_searchChangeFilter))
            {
                VisibleChanges = _changes;
            }
            else
            {
                var visible = new List<Models.Change>();
                foreach (var c in _changes)
                {
                    if (c.Path.Contains(_searchChangeFilter, StringComparison.OrdinalIgnoreCase))
                        visible.Add(c);
                }

                VisibleChanges = visible;
            }
        }

        [GeneratedRegex(@"^version https://git-lfs.github.com/spec/v\d+\r?\noid sha256:([0-9a-f]+)\r?\nsize (\d+)[\r\n]*$")]
        private static partial Regex REG_LFS_FORMAT();

        private static readonly HashSet<string> IMG_EXTS = new HashSet<string>()
        {
            ".ico", ".bmp", ".jpg", ".png", ".jpeg"
        };

        private Repository _repo = null;
        private int _activePageIndex = 0;
        private Models.Commit _commit = null;
        private string _fullMessage = string.Empty;
        private List<Models.Change> _changes = null;
        private List<Models.Change> _visibleChanges = null;
        private List<Models.Change> _selectedChanges = null;
        private string _searchChangeFilter = string.Empty;
        private DiffContext _diffContext = null;
        private object _viewRevisionFileContent = null;
        private Commands.Command.CancelToken _cancelToken = null;
    }
}
