# LasseVK.JobSystem

A class library for managing background jobs, complete with persistence, failure handling, retries, etc.

# For issues in this project

Execute the following command, and follow the instructions to connect to the kanban-board:

```
plink instructions
```

# Rules and guidelines for development

* All variables, class and type names, method and property names, comments, must be written in english
* Likewise for all error messages and other text that might be visible to developers or users of this code
* All code blocks must use {}, even if the block consists of only one statement, such as the then-part of an if-then statement.
* Prefer to solve only the challenges being presenter, and try to avoid solving future problem that may or may not occur. If there are significant advantages to tackle possible future changes already now, ask me.
* Challenge my assumptions, better to ask me for clarification or to discuss uncertainty or vague tasks than to blindly solve it.
* Compiler warnings must be fixed with the same priority as compiler errors.

* For now, the project is in initial development mode, which means signature- and breaking changes is OK. This message will be replaced with a more stringent message once we reach 1.0.

# Guidelines for version control

* Since the project is currently in initial development mode, use main for all work. We can clean up and squash history once we close in on 1.0. After 1.0 has been released, this message will be replaced with one that dictates how to handle new features.
* Logical and coherent commits are preferred
* If the output of a task logically solves multiple different topics, split into multiple commits, if possible